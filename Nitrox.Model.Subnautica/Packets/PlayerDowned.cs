using System;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1). A player's game sends it when its player would have died and went down
///     instead (both fields are ignored then: the server takes the sender). The server decides the window and sends it to
///     everyone; a window it doesn't grant (escalation, nobody to help) is answered with <see cref="PlayerRescueExpired" />
///     instead. A player who rejoins while down gets it again with the time that was left.
/// </summary>
[Serializable]
public sealed class PlayerDowned : Packet
{
    public SessionId PlayerId { get; }

    /// <summary>
    ///     Seconds left until the player dies unless revived. From the server only.
    /// </summary>
    public float WindowSeconds { get; }

    public PlayerDowned(SessionId playerId, float windowSeconds)
    {
        PlayerId = playerId;
        WindowSeconds = windowSeconds;
    }
}
