using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A creature hurt another player's body in the game that simulates the creature (a bite, or a crash fish
///     exploding). Bodies of other players can't take damage, so the hurt player's own game applies it (FIX_SPEC S14).
///     The server relays it only to that player, and only from the creature's simulating player.
/// </summary>
[Serializable]
public sealed class CreatureAttackedPlayer : Packet
{
    public NitroxId CreatureId { get; }
    public SessionId TargetSessionId { get; }

    /// <summary>
    ///     Damage before the hurt player's own reductions (dive suit, difficulty), which their game applies.
    /// </summary>
    public float Damage { get; }

    /// <summary>
    ///     The game's <c>DamageType</c>.
    /// </summary>
    public ushort DamageType { get; }

    public CreatureAttackedPlayer(NitroxId creatureId, SessionId targetSessionId, float damage, ushort damageType)
    {
        CreatureId = creatureId;
        TargetSessionId = targetSessionId;
        Damage = damage;
        DamageType = damageType;
    }
}
