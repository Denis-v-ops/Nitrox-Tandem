using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

public enum CraftingClaimOutcome : byte
{
    /// <summary>Not an answer (a field that was never filled in). Never taken for a grant.</summary>
    Unknown = 0,

    /// <summary>The server removed every item and told everyone: the craft can start.</summary>
    Granted = 1,

    /// <summary>At least one item isn't in a locker of the sub root anymore (someone took or moved it). Nothing was removed.</summary>
    Taken = 2,

    /// <summary>The server has crafting from lockers switched off (<c>CraftFromLockers</c>). Nothing was removed.</summary>
    Disabled = 3,

    /// <summary>The request makes no sense (no items, too many, the same item twice, not a base or Cyclops). Nothing was removed.</summary>
    Invalid = 4
}

/// <summary>
///     The server's answer to a <see cref="CraftingClaimRequest" />, sent to the claiming player only. A granted answer comes
///     after the <see cref="EntityDestroyed" /> packets of the claimed items (same delivery method and channel), so the
///     claimant's lockers already show the claim when it arrives. Never relayed from a client
///     (<c>DefaultServerPacketProcessor</c> blacklist).
/// </summary>
[Serializable]
public sealed class CraftingClaimResponse : Packet
{
    public int ClaimId { get; }

    public CraftingClaimOutcome Outcome { get; }

    /// <summary>For <see cref="CraftingClaimOutcome.Taken" />: the items that weren't available, so the sender can pick others.</summary>
    public List<NitroxId> UnavailableIds { get; }

    public CraftingClaimResponse(int claimId, CraftingClaimOutcome outcome, List<NitroxId> unavailableIds)
    {
        ClaimId = claimId;
        Outcome = outcome;
        UnavailableIds = unavailableIds;
    }
}
