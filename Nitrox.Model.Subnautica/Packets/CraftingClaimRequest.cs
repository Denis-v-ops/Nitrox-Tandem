using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Crafting from lockers (MULTINAUGHTICA_SPEC M8): a crafting station of a base or a Cyclops needs items that are only in
///     the lockers of that base or Cyclops. The client picks the exact items its own copy of the lockers holds; the server
///     checks that each one is still in a locker of that sub root, removes them all or none, tells everyone
///     (<see cref="EntityDestroyed" />) and then answers with <see cref="CraftingClaimResponse" />. The craft starts only after
///     a granted answer, so two players can't both get the last items of a locker.
/// </summary>
[Serializable]
public sealed class CraftingClaimRequest : Packet
{
    /// <summary>
    ///     Most items one claim may hold. A recipe needs a handful; the cap keeps a broken client from emptying a base with
    ///     one packet.
    /// </summary>
    public const int MAX_ITEMS = 64;

    /// <summary>The number the sender gave this claim; the answer carries it back.</summary>
    public int ClaimId { get; }

    /// <summary>The base (the id of its <c>Base</c>) or the Cyclops (its vehicle id) whose lockers the items come from.</summary>
    public NitroxId ScopeId { get; }

    /// <summary>The items, in the order they are spent.</summary>
    public List<NitroxId> ItemIds { get; }

    public CraftingClaimRequest(int claimId, NitroxId scopeId, List<NitroxId> itemIds)
    {
        ClaimId = claimId;
        ScopeId = scopeId;
        ItemIds = itemIds;
    }
}
