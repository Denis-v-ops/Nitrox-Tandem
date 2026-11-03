using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC B6: the propulsion and repulsion cannons' effects on the partner's copy of the held cannon. The
///     game plays them only in the game of the player who uses the cannon: the beam is turned on by
///     <c>PropulsionCannon.grabbedObject</c>'s setter and drawn by <c>PropulsionCannon.Update</c> while something is
///     grabbed, the shots play <c>VFXController.Play</c> and a sound. The partner's copy never grabs (that would run the
///     cannon's attraction physics with the partner's camera), so <see cref="RemotePropulsionBeam" /> draws the beam instead.
///     R2c (tool effects on the partner's body) can follow the same shape: a sender hook, one packet, one apply method.
/// </summary>
public class CannonEffects
{
    private readonly IPacketSender packetSender;

    public CannonEffects(IPacketSender packetSender)
    {
        this.packetSender = packetSender;
    }

    /// <summary>
    ///     The local player's handheld propulsion cannon grabbed <paramref name="target" /> (or let go, when null). The
    ///     Prawn suit's propulsion arm (no item id of its own) isn't covered.
    /// </summary>
    public void SendPropulsionGrabChanged(PropulsionCannon cannon, GameObject target)
    {
        PropulsionCannonWeapon weapon = cannon.GetComponentInParent<PropulsionCannonWeapon>();
        if (!weapon || !weapon.TryGetNitroxId(out NitroxId cannonId))
        {
            return;
        }

        if (!target)
        {
            packetSender.Send(new CannonEffect(cannonId, CannonEffectType.PROPULSION_RELEASE, null));
        }
        else if (target.TryGetNitroxId(out NitroxId targetId))
        {
            packetSender.Send(new CannonEffect(cannonId, CannonEffectType.PROPULSION_GRAB, targetId));
        }
    }

    public void SendPropulsionShot(PropulsionCannon cannon)
    {
        PropulsionCannonWeapon weapon = cannon.GetComponentInParent<PropulsionCannonWeapon>();
        if (weapon && weapon.TryGetNitroxId(out NitroxId cannonId))
        {
            packetSender.Send(new CannonEffect(cannonId, CannonEffectType.PROPULSION_SHOT, null));
        }
    }

    public void SendRepulsionShot(RepulsionCannon cannon)
    {
        if (cannon.TryGetNitroxId(out NitroxId cannonId))
        {
            packetSender.Send(new CannonEffect(cannonId, CannonEffectType.REPULSION_SHOT, null));
        }
    }

    /// <summary>Plays the effect on this game's copy of the partner's cannon.</summary>
    public static void Apply(CannonEffect packet)
    {
        if (!NitroxEntity.TryGetObjectFrom(packet.CannonId, out GameObject cannonObject))
        {
            return;
        }

        switch (packet.Effect)
        {
            case CannonEffectType.PROPULSION_GRAB when cannonObject.TryGetComponent(out PropulsionCannonWeapon weapon) && weapon.propulsionCannon:
                RemotePropulsionBeam beam = weapon.propulsionCannon.gameObject.EnsureComponent<RemotePropulsionBeam>();
                beam.Cannon = weapon.propulsionCannon;
                beam.Show(packet.TargetId);
                break;
            case CannonEffectType.PROPULSION_RELEASE when cannonObject.TryGetComponent(out PropulsionCannonWeapon weapon):
                HideBeam(weapon);
                break;
            case CannonEffectType.PROPULSION_SHOT when cannonObject.TryGetComponent(out PropulsionCannonWeapon weapon) && weapon.propulsionCannon:
                HideBeam(weapon);
                // As PropulsionCannon.OnShoot
                weapon.propulsionCannon.fxControl.Play(0);
                Utils.PlayFMODAsset(weapon.propulsionCannon.shootSound, weapon.propulsionCannon.transform);
                break;
            case CannonEffectType.REPULSION_SHOT when cannonObject.TryGetComponent(out RepulsionCannon repulsionCannon):
                // As RepulsionCannon.OnToolUseAnim (its Update plays the bubbles one frame later)
                repulsionCannon.fxControl.Play();
                Utils.PlayOneShotPS(repulsionCannon.bubblesFX, repulsionCannon.muzzle.position, repulsionCannon.muzzle.rotation);
                Utils.PlayFMODAsset(repulsionCannon.shootSound, repulsionCannon.transform);
                break;
            default:
                Log.Debug($"[{nameof(CannonEffects)}] {packet.Effect} doesn't fit {cannonObject.name}");
                return;
        }
        Log.Debug($"[{nameof(CannonEffects)}] Played {packet.Effect} on the copy of {packet.CannonId}");
    }

    private static void HideBeam(PropulsionCannonWeapon weapon)
    {
        if (weapon.propulsionCannon && weapon.propulsionCannon.TryGetComponent(out RemotePropulsionBeam beam))
        {
            beam.Hide();
        }
    }
}
