using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     COOP_FEATURES_SPEC B6: what a player's handheld propulsion or repulsion cannon shows, so the partner's copy of the
///     held cannon shows it too. Cosmetic only: the object's movement goes through simulation locks and position updates.
///     Not persisted (relayed by the server's default processor).
/// </summary>
[Serializable]
public sealed class CannonEffect : Packet
{
    /// <summary>The held cannon's item id (a <c>PropulsionCannonWeapon</c> or a <c>RepulsionCannon</c>).</summary>
    public NitroxId CannonId { get; }

    public CannonEffectType Effect { get; }

    /// <summary>The grabbed object, for <see cref="CannonEffectType.PROPULSION_GRAB" />; null otherwise.</summary>
    public NitroxId? TargetId { get; }

    public CannonEffect(NitroxId cannonId, CannonEffectType effect, NitroxId? targetId)
    {
        CannonId = cannonId;
        Effect = effect;
        TargetId = targetId;
    }

    public override string ToString()
    {
        return $"[CannonEffect - CannonId: {CannonId}, Effect: {Effect}, TargetId: {TargetId}]";
    }
}

public enum CannonEffectType
{
    /// <summary>The propulsion cannon's beam turns on and follows the grabbed object.</summary>
    PROPULSION_GRAB,

    /// <summary>The beam turns off (the object was let go).</summary>
    PROPULSION_RELEASE,

    /// <summary>The grabbed object was shot away: beam off, muzzle flash and sound.</summary>
    PROPULSION_SHOT,

    /// <summary>A repulsion shot: muzzle flash, bubbles and sound.</summary>
    REPULSION_SHOT
}
