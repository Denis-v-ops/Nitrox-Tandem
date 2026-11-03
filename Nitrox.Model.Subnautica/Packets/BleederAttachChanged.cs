using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A bleeder latched onto a player or let go of them (FIX_SPEC S15d). Sent by the game that simulates the bleeder
///     when it starts its attach or detaches; the other games show their copy on that player. The latched player's game
///     also sends a detach when it knocks its copy off or can't hold it (in a base, a vehicle, downed), and the simulating
///     game then lets go. No server processor: every other game needs it, which is the default relay.
/// </summary>
[Serializable]
public sealed class BleederAttachChanged : Packet
{
    public NitroxId BleederId { get; }
    public SessionId PlayerId { get; }
    public bool Attached { get; }

    public BleederAttachChanged(NitroxId bleederId, SessionId playerId, bool attached)
    {
        BleederId = bleederId;
        PlayerId = playerId;
        Attached = attached;
    }
}
