using System;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): the server accepted a revive. Sent to everyone; the revived player's
///     game gets up with <see cref="Health" />, the others end the downed pose and the distress call (D3).
/// </summary>
[Serializable]
public sealed class PlayerRevived : Packet
{
    public SessionId PlayerId { get; }

    public SessionId RescuerId { get; }

    public float Health { get; }

    public PlayerRevived(SessionId playerId, SessionId rescuerId, float health)
    {
        PlayerId = playerId;
        RescuerId = rescuerId;
        Health = health;
    }
}
