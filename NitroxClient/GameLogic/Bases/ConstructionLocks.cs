using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Simulation;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic.Bases;

/// <summary>
///     COOP_FEATURES_SPEC B2: one player at a time builds or deconstructs a ghost or module. Each game charges materials when
///     its own <see cref="Constructable.constructedAmount" /> crosses a resource step (<c>Constructable.Construct</c>, refunds in
///     <c>Constructable.DeconstructAsync</c>), and the partner's amount overwrites the local one
///     (<see cref="BuildingHandler.ProgressConstruction" />), often moving it back below a step that game already paid for. So
///     two builders on one ghost pay for the same steps more than once. The builder takes the object's exclusive lock first,
///     like a fabricator's crafting player (<c>GhostCrafter_OnCraftingBegin_Patch</c>), and gives it back as a transient lock
///     once it stops building for a moment or the object is finished.
/// </summary>
public sealed class ConstructionLocks
{
    private const float RELEASE_AFTER_IDLE_SECONDS = 1f;
    private const float RETRY_AFTER_DENIED_SECONDS = 1f;

    private readonly SimulationOwnership simulationOwnership;
    private readonly Dictionary<NitroxId, float> lastUseById = [];
    private readonly HashSet<NitroxId> pendingIds = [];
    private readonly Dictionary<NitroxId, float> deniedUntilById = [];

    public ConstructionLocks(SimulationOwnership simulationOwnership)
    {
        this.simulationOwnership = simulationOwnership;
    }

    /// <summary>
    ///     True if this game may build or deconstruct <paramref name="constructable" /> this frame. Otherwise asks for its lock
    ///     (building starts a few frames later) or, while the partner is building it, says so.
    /// </summary>
    public bool MayConstruct(Constructable constructable)
    {
        if (!constructable.TryGetNitroxId(out NitroxId id))
        {
            return true;
        }
        if (simulationOwnership.HasExclusiveLock(id))
        {
            bool releaseScheduled = lastUseById.ContainsKey(id);
            lastUseById[id] = Time.time;
            if (!releaseScheduled)
            {
                CoroutineHost.StartCoroutine(ReleaseWhenDone(constructable, id));
            }
            return true;
        }
        if (pendingIds.Contains(id) || (deniedUntilById.TryGetValue(id, out float deniedUntil) && Time.time < deniedUntil))
        {
            return false;
        }

        pendingIds.Add(id);
        simulationOwnership.RequestSimulationLock(new LockRequest<Context>(id, SimulationLockType.EXCLUSIVE, OnLockResponse, new Context()));
        return false;
    }

    private void OnLockResponse(NitroxId id, bool lockAcquired, Context context)
    {
        pendingIds.Remove(id);
        if (lockAcquired)
        {
            deniedUntilById.Remove(id);
            return;
        }
        deniedUntilById[id] = Time.time + RETRY_AFTER_DENIED_SECONDS;
        Log.InGame(Language.main.Get("Nitrox_DenyOwnershipHand"));
    }

    private IEnumerator ReleaseWhenDone(Constructable constructable, NitroxId id)
    {
        while (constructable && !constructable.constructed && lastUseById.TryGetValue(id, out float lastUse) && Time.time - lastUse < RELEASE_AFTER_IDLE_SECONDS)
        {
            yield return null;
        }
        lastUseById.Remove(id);
        // A finished base piece or module may already have been given back as a transient lock (BuildingProcessor.TryClaimBuildPieceAsync)
        if (simulationOwnership.HasExclusiveLock(id))
        {
            simulationOwnership.RequestSimulationLock(id, SimulationLockType.TRANSIENT);
        }
    }

    private sealed class Context : LockRequestContext;
}
