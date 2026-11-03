using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     FIX_SPEC S15d: a bleeder latches onto either player. Only the game that simulates a bleeder decides its attach
///     (<c>game:AttachAndSuck.cs</c>): on its own player through the game's collision, on a partner's body through the
///     body's trigger (<see cref="BodyBleederTarget" />), with the checks <c>OnCollisionEnter</c> makes, taken from the
///     body's state. Its suck on a body reaches the partner through S14's <c>CreatureAttackedPlayer</c>. The other games
///     mirror the attach on their copy (<see cref="BleederAttachChanged" />): vanilla <c>AttachAndSuck.Update</c> then
///     moves the copy onto that player, and on the local player it marks the game's own attach target, so the arms play
///     the bleeder fight and a hit (<c>ArmsController.OnToolBleederHitAnim</c>) hurts the copy. Killing it is broadcast as
///     any non-simulator kill is; any other detach of the copy on the local player (a cannon, a base, a vehicle, downed)
///     is sent back to the simulating game, which lets go.
/// </summary>
public class Bleeders
{
    private readonly SimulationOwnership simulationOwnership;
    private readonly IPacketSender packetSender;
    private readonly LocalPlayer localPlayer;
    private readonly PlayerManager playerManager;

    public Bleeders(SimulationOwnership simulationOwnership, IPacketSender packetSender, LocalPlayer localPlayer, PlayerManager playerManager)
    {
        this.simulationOwnership = simulationOwnership;
        this.packetSender = packetSender;
        this.localPlayer = localPlayer;
        this.playerManager = playerManager;
    }

    /// <summary>
    ///     True when this game decides what the bleeder does (or it has no id, as in single player).
    /// </summary>
    public bool IsSimulatedHere(AttachAndSuck attachAndSuck)
    {
        return !attachAndSuck.TryGetNitroxId(out NitroxId id) || simulationOwnership.HasAnyLockType(id);
    }

    /// <summary>
    ///     The checks <c>AttachAndSuck.OnCollisionEnter</c> makes for a <see cref="Player" /> (not in a sub, normal mode,
    ///     can be attacked), with the body's state, plus T1: a downed partner can't be latched onto.
    /// </summary>
    public static bool CanHold(RemotePlayer body)
    {
        return body.CanBeAttacked() && !body.Vehicle && !body.PilotingChair && !body.IsDowned;
    }

    /// <summary>
    ///     The same checks for the local player, plus T1.
    /// </summary>
    public static bool CanHoldLocalPlayer()
    {
        Player player = Player.main;
        return player && !player.IsInSub() && player.GetMode() == Player.Mode.Normal && player.CanBeAttacked() && !RescueWindow.IsLocalPlayerDowned;
    }

    /// <summary>
    ///     A simulated bleeder touched a partner's body (the body's collider is a trigger, so the game's
    ///     <c>OnCollisionEnter</c> never runs for it). Sets the target as <c>OnCollisionEnter</c> does.
    /// </summary>
    public void TryAttachToBody(AttachAndSuck attachAndSuck, BodyBleederTarget body)
    {
        float time = Time.time;
        BleederAttachTarget target = body.Target;
        if (!target || target.occupied || attachAndSuck.currentTarget || attachAndSuck.timeDetached + 4f >= time ||
            !attachAndSuck.TryGetComponent(out LiveMixin bleederLiveMixin) || !bleederLiveMixin.IsAlive() ||
            !CanHold(body.Partner) || !IsSimulatedHere(attachAndSuck))
        {
            return;
        }

        SetTarget(attachAndSuck, target, body.Partner.LiveMixin, time);
    }

    /// <summary>
    ///     The bleeder starts its attach in this game, which simulates it (<c>AttachAndSuck.StartPerform</c>).
    /// </summary>
    public void OnAttachStarted(AttachAndSuck attachAndSuck)
    {
        if (attachAndSuck.TryGetNitroxId(out NitroxId id) && simulationOwnership.HasAnyLockType(id) && TryGetPlayerId(attachAndSuck.currentTarget, out SessionId playerId))
        {
            packetSender.Send(new BleederAttachChanged(id, playerId, true));
        }
    }

    /// <summary>
    ///     Before <c>AttachAndSuck.SetDetached</c>: the simulating game tells the others; a copy on the local player tells
    ///     the simulating game (it was knocked off, or the player can't hold it).
    /// </summary>
    public void OnDetaching(AttachAndSuck attachAndSuck)
    {
        BleederAttachTarget target = attachAndSuck.currentTarget;
        if (!target || !attachAndSuck.TryGetNitroxId(out NitroxId id))
        {
            return;
        }
        bool simulated = simulationOwnership.HasAnyLockType(id);
        if ((simulated || target == LocalTarget) && TryGetPlayerId(target, out SessionId playerId))
        {
            packetSender.Send(new BleederAttachChanged(id, playerId, false));
        }
    }

    /// <summary>
    ///     After <c>AttachAndSuck.Update</c>, while the bleeder holds or approaches a target. The game checks only that its
    ///     own player went into a walkable place (a base, the Cyclops) or a dry precursor area; this adds the partner's
    ///     body (base, life pod, vehicle, Cyclops helm, downed) and a downed local player (T1), and keeps a bleeder on a
    ///     body on the default layer (the game draws it on the view model layer for its own player's first-person view).
    /// </summary>
    public void AfterUpdate(AttachAndSuck attachAndSuck)
    {
        BleederAttachTarget target = attachAndSuck.currentTarget;
        if (target == LocalTarget)
        {
            if (!CanHoldLocalPlayer() && (RescueWindow.IsLocalPlayerDowned || !IsSimulatedHere(attachAndSuck)))
            {
                attachAndSuck.SetDetached();
            }
            return;
        }
        BodyBleederTarget body = target.GetComponentInParent<BodyBleederTarget>();
        if (!body)
        {
            return;
        }
        if (!CanHold(body.Partner))
        {
            attachAndSuck.SetDetached();
            if (IsSimulatedHere(attachAndSuck))
            {
                // As the game does when its player goes inside
                attachAndSuck.transform.position = attachAndSuck.creature.leashPosition;
            }
            return;
        }
        if (attachAndSuck.attached && attachAndSuck.bleeder.modelRenderer.gameObject.layer != LayerID.Default)
        {
            attachAndSuck.SetLayer(LayerID.Default);
        }
    }

    /// <summary>
    ///     Another game's bleeder latched onto a player or let go (<see cref="BleederAttachChanged" />).
    /// </summary>
    public void Apply(BleederAttachChanged packet)
    {
        if (!NitroxEntity.TryGetComponentFrom(packet.BleederId, out AttachAndSuck attachAndSuck))
        {
            return;
        }
        BleederAttachTarget target = GetTarget(packet.PlayerId, out LiveMixin targetLiveMixin);
        bool simulated = simulationOwnership.HasAnyLockType(packet.BleederId);

        if (!packet.Attached)
        {
            if (!target || attachAndSuck.currentTarget != target)
            {
                return;
            }
            if (simulated)
            {
                // The latched player knocked it off or can't hold it: let go, and tell everyone (SetDetached's patch)
                attachAndSuck.SetDetached();
                return;
            }
            // The simulating game let go
            using (PacketSuppressor<BleederAttachChanged>.Suppress())
            {
                attachAndSuck.SetDetached();
            }
            return;
        }

        // An attach sent before the lock came to us: this game decides now
        if (simulated || !target || attachAndSuck.currentTarget == target)
        {
            return;
        }

        if (target == LocalTarget && (!CanHoldLocalPlayer() || (target.occupied && target.bleeder && target.bleeder != attachAndSuck.bleeder)))
        {
            // We're inside or downed by now, or another bleeder holds us already: the simulating game must let go
            packetSender.Send(new BleederAttachChanged(packet.BleederId, packet.PlayerId, false));
            return;
        }

        using (PacketSuppressor<BleederAttachChanged>.Suppress())
        {
            if (attachAndSuck.currentTarget)
            {
                attachAndSuck.SetDetached();
            }
            SetTarget(attachAndSuck, target, targetLiveMixin, Time.time);
            // What StartPerform does, which runs only in the simulating game. Update then moves the copy onto the player.
            // Perform (the suck) still runs only there; if the lock comes here, the game's own Evaluate picks this target up.
            attachAndSuck.GetComponent<Rigidbody>().isKinematic = true;
            attachAndSuck.GetComponent<Collider>().enabled = false;
            attachAndSuck.active = true;
            // The simulating game's poses (where it saw the player a moment ago) would put the copy back off the player
            // every physics step until Update moves it on again
            SetFollowingRemotePoses(attachAndSuck, false);
        }
    }

    /// <summary>
    ///     A copy that is mirrored on a player follows that player, not the simulating game's poses; it goes back to them
    ///     when it lets go (<c>AttachAndSuck_SetDetached_Patch</c>).
    /// </summary>
    public static void SetFollowingRemotePoses(AttachAndSuck attachAndSuck, bool follow)
    {
        if (attachAndSuck.TryGetComponent(out RemotelyControlled remotelyControlled))
        {
            remotelyControlled.enabled = follow;
        }
    }

    private BleederAttachTarget? LocalTarget => Player.main && Player.main.armsController ? Player.main.armsController.bleederAttackTarget : null;

    private static void SetTarget(AttachAndSuck attachAndSuck, BleederAttachTarget target, LiveMixin targetLiveMixin, float time)
    {
        // Code from AttachAndSuck.OnCollisionEnter (game 83031)
        attachAndSuck.currentTarget = target;
        attachAndSuck.targetLiveMixin = targetLiveMixin;
        attachAndSuck.attached = false;
        attachAndSuck.startPos = attachAndSuck.transform.position;
        attachAndSuck.startRot = attachAndSuck.transform.rotation;
        attachAndSuck.timeTargetSet = time;
        target.occupied = true;
        target.attached = false;
        target.bleeder = attachAndSuck.bleeder;
    }

    private bool TryGetPlayerId(BleederAttachTarget target, out SessionId playerId)
    {
        playerId = default;
        if (target == LocalTarget && localPlayer.SessionId.HasValue)
        {
            playerId = localPlayer.SessionId.Value;
            return true;
        }
        BodyBleederTarget body = target ? target.GetComponentInParent<BodyBleederTarget>() : null;
        if (body)
        {
            playerId = body.Partner.SessionId;
            return true;
        }
        return false;
    }

    private BleederAttachTarget? GetTarget(SessionId playerId, out LiveMixin targetLiveMixin)
    {
        targetLiveMixin = null;
        if (localPlayer.SessionId.HasValue && localPlayer.SessionId.Value == playerId)
        {
            targetLiveMixin = Player.main ? Player.main.liveMixin : null;
            return LocalTarget;
        }
        if (playerManager.TryFind(playerId, out RemotePlayer remotePlayer) && remotePlayer.Body && remotePlayer.Body.TryGetComponent(out BodyBleederTarget body))
        {
            targetLiveMixin = remotePlayer.LiveMixin;
            return body.Target;
        }
        return null;
    }
}
