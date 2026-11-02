using System;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     The pain sound a player's own game played when they got hurt, to play on their body for the others
///     (COOP_FEATURES_SPEC D1). Relayed as is: nothing to persist.
/// </summary>
[Serializable]
public class PlayerPainSound : Packet
{
    public SessionId SessionId { get; }
    public PainKind Kind { get; }

    public PlayerPainSound(SessionId sessionId, PainKind kind)
    {
        SessionId = sessionId;
        Kind = kind;
    }

    /// <summary>
    ///     The emitters of the game's <c>PlayerDamageSounds</c>.
    /// </summary>
    public enum PainKind : byte
    {
        WITH_TANK,
        WITHOUT_TANK,
        WITHOUT_TANK_LIGHT,
        WITHOUT_MASK,
        SMOKE
    }
}
