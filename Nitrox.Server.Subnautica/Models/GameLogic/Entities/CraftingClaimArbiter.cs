using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Decides a claim for crafting from lockers (<see cref="CraftingClaimRequest" />, MULTINAUGHTICA_SPEC M8). The crafting
///     player's game picked the exact items it needs from the lockers of a base or a Cyclops. Each game only knows its own
///     copy of the lockers, so two players crafting at the same moment can pick the same last items; nobody may get an item
///     twice.
/// </summary>
/// <remarks>
///     <para>
///         All or nothing. The scope must be a base (<see cref="BuildEntity" />) or a Cyclops. Every item must be an
///         <see cref="InventoryItemEntity" /> whose parents lead to the scope without passing a player (an inventory), a
///         planter, another vehicle (a docked Seamoth or Prawn Suit), a base machine (<see cref="InteriorPieceEntity" />:
///         bioreactor, reactor, water park, filtration machine; <see cref="MapRoomEntity" />: scanner room) or a module that
///         holds things but is no locker (<see cref="notLockerTechTypes" />). If one doesn't, nothing is removed and the answer
///         lists the ones that failed, so the game can pick others. Claims are decided one at a time (<see cref="gate" />):
///         the second of two claims for the same item finds it gone.
///     </para>
///     <para>
///         The removal is a callback (the processor passes the same removal an <see cref="EntityDestroyed" /> does). Every
///         item is checked again right before its removal; an item that is gone or moved by then, or a removal that throws,
///         fails the claim and the items removed so far go back to their parents.
///     </para>
///     <para>
///         <see cref="ClaimAsync" /> also keeps the delivery of one claim (its removals and its answer) before the next claim
///         is decided, so the loser of two claims hears of the winner's removals before its own refusal.
///     </para>
///     <para>
///         A granted claim is remembered (in memory, the last <see cref="MAX_KEPT_GRANTS" />) so that its game can give it
///         back once if the craft can't start after all (<see cref="Return" />): the same entities go back to the lockers
///         they came from, with their ids and state (a battery keeps its charge).
///     </para>
/// </remarks>
internal sealed class CraftingClaimArbiter
{
    /// <summary>How far up an item's parents are followed; a locker item is two or three levels below its base.</summary>
    private const int MAX_DEPTH = 16;

    /// <summary>Granted claims kept for a possible return. A return follows its grant within a network round trip (or the game's 15 s timeout).</summary>
    internal const int MAX_KEPT_GRANTS = 32;

    /// <summary>
    ///     Base modules that hold items but are no lockers, by tech type. Machines that are part of the base itself are
    ///     <see cref="InteriorPieceEntity" />s, the scanner room a <see cref="MapRoomEntity" />, planters
    ///     <see cref="PlanterEntity" />s; lockers, wall lockers and these are <see cref="ModuleEntity" />s.
    /// </summary>
    private static readonly HashSet<string> notLockerTechTypes = new(StringComparer.Ordinal)
    {
        "Aquarium",
        "Trashcans",
        "LabTrashcan"
    };

    private readonly EntityRegistry entityRegistry;
    private readonly object gate = new();
    private readonly SemaphoreSlim exclusive = new(1, 1);
    private readonly Dictionary<(SessionId Session, int ClaimId), Grant> grants = [];
    private readonly Queue<(SessionId Session, int ClaimId)> grantOrder = new();

    public CraftingClaimArbiter(EntityRegistry entityRegistry)
    {
        this.entityRegistry = entityRegistry;
    }

    /// <summary>
    ///     Decides a claim and delivers its outcome before the next claim is decided.
    /// </summary>
    /// <param name="deliver">Sends what the decision needs sent; called for every outcome, while no other claim is decided.</param>
    public async Task<CraftingClaimDecision> ClaimAsync(SessionId claimant, int claimId, NitroxId? scopeId, IReadOnlyList<NitroxId>? itemIds, Func<NitroxId, Entity?> remove,
                                                        Func<CraftingClaimDecision, Task> deliver)
    {
        await exclusive.WaitAsync();
        try
        {
            CraftingClaimDecision decision = Claim(claimant, claimId, scopeId, itemIds, remove);
            await deliver(decision);
            return decision;
        }
        finally
        {
            exclusive.Release();
        }
    }

    /// <param name="claimant">The claiming player's session; a grant is remembered under it and the claim id for <see cref="Return" />.</param>
    /// <param name="scopeId">The base or the Cyclops the items must be in.</param>
    /// <param name="itemIds">The items, in the order they are spent.</param>
    /// <param name="remove">Removes an entity from the world, or returns null when it isn't there anymore.</param>
    public CraftingClaimDecision Claim(SessionId claimant, int claimId, NitroxId? scopeId, IReadOnlyList<NitroxId>? itemIds, Func<NitroxId, Entity?> remove)
    {
        if (scopeId == null || itemIds == null || itemIds.Count == 0 || itemIds.Count > CraftingClaimRequest.MAX_ITEMS || HasDuplicatesOrNulls(itemIds))
        {
            return CraftingClaimDecision.Invalid;
        }

        lock (gate)
        {
            if (!entityRegistry.TryGetEntityById(scopeId, out Entity? scope) || !IsScope(scope))
            {
                return CraftingClaimDecision.Invalid;
            }

            List<NitroxId> unavailable = [];
            foreach (NitroxId itemId in itemIds)
            {
                if (!IsClaimable(scopeId, itemId))
                {
                    unavailable.Add(itemId);
                }
            }
            if (unavailable.Count > 0)
            {
                return new CraftingClaimDecision(CraftingClaimOutcome.Taken, unavailable, []);
            }

            List<Entity> removed = new(itemIds.Count);
            List<NitroxId?> parentsOfRemoved = new(itemIds.Count);
            foreach (NitroxId itemId in itemIds)
            {
                // The removal takes the entity out of its parent and clears the link, so the parent is kept for a put back
                NitroxId? parentId = entityRegistry.TryGetEntityById(itemId, out Entity? current) ? current.ParentId : null;
                Entity? entity;
                try
                {
                    entity = IsClaimable(scopeId, itemId) ? remove(itemId) : null;
                }
                catch (Exception)
                {
                    PutBack(removed, parentsOfRemoved, null);
                    return CraftingClaimDecision.Invalid;
                }

                if (entity == null)
                {
                    // Gone or moved since the check: all or nothing
                    PutBack(removed, parentsOfRemoved, null);
                    return new CraftingClaimDecision(CraftingClaimOutcome.Taken, [itemId], []);
                }
                removed.Add(entity);
                parentsOfRemoved.Add(parentId);
            }

            RememberGrant(claimant, claimId, new Grant(removed, parentsOfRemoved));
            return new CraftingClaimDecision(CraftingClaimOutcome.Granted, [], removed);
        }
    }

    /// <summary>
    ///     Puts the items of a granted claim back where they were taken from, once. An item whose locker is gone meanwhile
    ///     goes to <paramref name="fallbackParentId" /> (the claimant's inventory). Returns the entities put back (to spawn them
    ///     for everyone), or nothing when the claim isn't known (never granted, already returned, or forgotten).
    /// </summary>
    public List<Entity> Return(SessionId claimant, int claimId, NitroxId fallbackParentId)
    {
        lock (gate)
        {
            if (!grants.Remove((claimant, claimId), out Grant? grant))
            {
                return [];
            }
            return PutBack(grant.Removed, grant.Parents, fallbackParentId);
        }
    }

    private void RememberGrant(SessionId claimant, int claimId, Grant grant)
    {
        (SessionId, int) key = (claimant, claimId);
        if (!grants.ContainsKey(key))
        {
            grantOrder.Enqueue(key);
        }
        grants[key] = grant;
        while (grantOrder.Count > MAX_KEPT_GRANTS)
        {
            grants.Remove(grantOrder.Dequeue());
        }
    }

    private List<Entity> PutBack(List<Entity> removed, List<NitroxId?> parentsOfRemoved, NitroxId? fallbackParentId)
    {
        for (int i = 0; i < removed.Count; i++)
        {
            NitroxId? parentId = parentsOfRemoved[i];
            if (fallbackParentId != null && (parentId == null || !entityRegistry.TryGetEntityById(parentId, out Entity? _)))
            {
                parentId = fallbackParentId;
            }
            removed[i].ParentId = parentId;
            entityRegistry.AddOrUpdate(removed[i]);
        }
        return removed;
    }

    /// <summary>
    ///     The sub roots whose lockers can be claimed from: a base, or a Cyclops.
    /// </summary>
    internal static bool IsScope(Entity scope) => scope is BuildEntity or VehicleEntity { TechType.Name: "Cyclops" };

    private static bool HasDuplicatesOrNulls(IReadOnlyList<NitroxId> itemIds)
    {
        HashSet<NitroxId> seen = [];
        foreach (NitroxId? itemId in itemIds)
        {
            if (itemId == null || !seen.Add(itemId))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    ///     An item can be claimed when it is an item of a container and its parents lead to <paramref name="scopeId" />
    ///     without a player, a planter, a vehicle that isn't the scope itself, a base machine or a module that is no locker.
    ///     The walk stops at the scope before it looks at what it passes, which is why the scope itself must be a base or a
    ///     Cyclops (<see cref="IsScope" />).
    /// </summary>
    internal bool IsClaimable(NitroxId scopeId, NitroxId itemId)
    {
        if (!entityRegistry.TryGetEntityById(itemId, out InventoryItemEntity? item))
        {
            return false;
        }

        NitroxId? parentId = item.ParentId;
        for (int depth = 0; depth < MAX_DEPTH && parentId != null; depth++)
        {
            if (parentId.Equals(scopeId))
            {
                return true;
            }
            if (!entityRegistry.TryGetEntityById(parentId, out Entity? parent) ||
                parent is PlayerEntity or PlanterEntity or VehicleEntity or InteriorPieceEntity or MapRoomEntity ||
                (parent.TechType != null && notLockerTechTypes.Contains(parent.TechType.Name)))
            {
                return false;
            }
            parentId = parent.ParentId;
        }
        return false;
    }

    private sealed record Grant(List<Entity> Removed, List<NitroxId?> Parents);
}

internal readonly record struct CraftingClaimDecision(CraftingClaimOutcome Outcome, IReadOnlyList<NitroxId> Unavailable, IReadOnlyList<Entity> Removed)
{
    public static CraftingClaimDecision Invalid { get; } = new(CraftingClaimOutcome.Invalid, [], []);
}
