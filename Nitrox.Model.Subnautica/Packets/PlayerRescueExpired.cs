using System;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1) ended without a revive. From the server to everyone: the downed player's
///     game lets the kill through (vanilla death, Nitrox's death beacon), the others end the downed pose and the distress
///     call (D3). From a downed player's game to the server: the player died while down (drowned); the server ends the
///     window and tells the others (the field is ignored, the server takes the sender).
/// </summary>
[Serializable]
public sealed class PlayerRescueExpired : Packet
{
    public SessionId PlayerId { get; }

    public PlayerRescueExpired(SessionId playerId)
    {
        PlayerId = playerId;
    }
}
