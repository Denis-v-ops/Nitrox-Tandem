using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): a player finished holding the interact button on a downed partner. Sent
///     to the server only. It checks that the two can reach each other, that the target is down and, for a first aid kit,
///     that the kit is in the rescuer's inventory; it destroys the kit and sends <see cref="PlayerRevived" /> to everyone.
/// </summary>
[Serializable]
public sealed class PlayerReviveRequest : Packet
{
    public SessionId TargetId { get; }

    public ReviveMethod Method { get; }

    /// <summary>
    ///     The first aid kit the rescuer holds, for <see cref="ReviveMethod.FIRST_AID_KIT" />.
    /// </summary>
    public Optional<NitroxId> KitId { get; }

    public PlayerReviveRequest(SessionId targetId, ReviveMethod method, Optional<NitroxId> kitId)
    {
        TargetId = targetId;
        Method = method;
        KitId = kitId;
    }

    public enum ReviveMethod : byte
    {
        /// <summary>A 6 s hold, about 20 health.</summary>
        HAND,

        /// <summary>A 2 s hold with a first aid kit in hand, 50 health; the kit is used up.</summary>
        FIRST_AID_KIT
    }
}
