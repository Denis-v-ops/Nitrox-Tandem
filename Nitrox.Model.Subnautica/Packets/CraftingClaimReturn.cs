using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Crafting from lockers (MULTINAUGHTICA_SPEC M8): a granted claim whose craft couldn't start after all (the station got
///     busy or lost power, the inventory lost its share, the answer came too late). The server puts the claimed items back
///     where it took them from (the same entities, with their ids and state) and spawns them for everyone. Once per claim;
///     a second return or one for a claim the server doesn't know does nothing.
/// </summary>
[Serializable]
public sealed class CraftingClaimReturn : Packet
{
    public int ClaimId { get; }

    public CraftingClaimReturn(int claimId)
    {
        ClaimId = claimId;
    }
}
