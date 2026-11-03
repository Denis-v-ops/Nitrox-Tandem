using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5): a player holds on to the outside of the Seamoth a partner pilots. The
///     Seamoth has one seat, and the vehicle's own way of carrying a player (<c>Player.EnterLockedMode</c>) makes the game
///     treat the player as out of the water, so it isn't used: the hitcher stays a swimmer in <c>Normal</c> mode (air
///     drains, creatures can still go for them) whose motor is switched off and who is put on a handle on the hull every
///     frame. Every other game hangs the hitcher's body on the same handle (<see cref="RemotePlayer.SetHitched" />).
/// </summary>
/// <remarks>
///     <para>
///         Grab on: look at the hull of a Seamoth a partner pilots and press the interact button (<c>Vehicle.OnHandHover</c>,
///         <c>Vehicle.OnHandClick</c> patches). The game asks the server (<see cref="PlayerHitched" />) and only grabs on
///         when the answer comes.
///     </para>
///     <para>
///         Let go: the Exit key, going down (T1), the Seamoth docking, destroyed or gone, the pilot out of it, the
///         hitcher dying or in a cinematic. The game sends where it let go (<see cref="PlayerUnhitched" />). The server also
///         ends a hitch itself (the pilot got out or left, the Seamoth docked or was destroyed) and tells this game.
///     </para>
///     <para>
///         The hitcher's own player isn't parented to the Seamoth: it is placed on the handle in <c>LateUpdate</c>, after
///         this game's copy of the Seamoth moved. A Seamoth destroyed in this game can't take the player down with it, and
///         the player's rotation (where the camera looks) stays free. The hitcher keeps sending its movement, so the
///         server always has its position (partner checks, distress calls); the other games ignore it while the body
///         hangs on the handle.
///     </para>
/// </remarks>
public sealed class SeamothHitch
{
    /// <summary>
    ///     Where the hitcher hangs, in the Seamoth's space: on the left side of the hull, beside the cockpit. (H) measured
    ///     on the rig: the hull's colliders end about 1.4 m to the side of the Seamoth's origin.
    /// </summary>
    public static readonly Vector3 HandleLocalPosition = new(-1.9f, 0.1f, -0.3f);

    public const string HANDLE_NAME = "NitroxHitchHandle";

    /// <summary>
    ///     A request the server didn't answer in this time can be sent again.
    /// </summary>
    public const float REQUEST_TIMEOUT_SECONDS = 3f;

    /// <summary>
    ///     Nobody pilots the Seamoth for this long: the hitcher lets go. The server says so too when the pilot gets out; this
    ///     covers the cases it doesn't see (a pilot pushed out without a packet).
    /// </summary>
    public const float NO_PILOT_SECONDS = 1.5f;

    private readonly LocalPlayer localPlayer;
    private readonly IPacketSender packetSender;
    private readonly PlayerManager playerManager;

    private SeaMoth? vehicle;
    private Transform? handle;
    private bool controllerWasEnabled;
    private NitroxId? pendingVehicleId;
    private float pendingSince;
    private float noPilotSince = -1;

    public SeamothHitch(LocalPlayer localPlayer, IPacketSender packetSender, PlayerManager playerManager)
    {
        this.localPlayer = localPlayer;
        this.packetSender = packetSender;
        this.playerManager = playerManager;
        // Static for the patches and the dev bridge: a new session starts up
        LocalVehicleId = null;
        LastReleaseReason = string.Empty;
    }

    /// <summary>
    ///     The Seamoth the local player holds on to; null when none. Static for the patches on hot paths
    ///     (<c>Player.IsFreeToInteract</c>) and the dev bridge.
    /// </summary>
    public static NitroxId? LocalVehicleId { get; private set; }

    public static bool IsLocalPlayerHitched => LocalVehicleId != null;

    /// <summary>
    ///     Why the local player let go last (for the dev bridge).
    /// </summary>
    public static string LastReleaseReason { get; private set; } = string.Empty;

    /// <summary>
    ///     The hand reticle on a Seamoth's hull. True if this game shows "hold on" (the original hover must not run).
    /// </summary>
    public bool TryHover(Vehicle target)
    {
        if (!CanHoldOn(target, out _))
        {
            return false;
        }
        HandReticle.main.SetIcon(HandReticle.IconType.Hand);
        HandReticle.main.SetText(HandReticle.TextType.Hand, "Nitrox_HitchHoldOn", true, GameInput.Button.LeftHand);
        HandReticle.main.SetText(HandReticle.TextType.HandSubscript, "Nitrox_HitchHoldOnHint", true);
        return true;
    }

    /// <summary>
    ///     A click on a Seamoth's hull. True if it asked to hold on (the original click, entering the vehicle, must not run).
    /// </summary>
    public bool TryClick(Vehicle target)
    {
        if (!CanHoldOn(target, out NitroxId vehicleId))
        {
            return false;
        }
        if (pendingVehicleId == null || Time.unscaledTime - pendingSince > REQUEST_TIMEOUT_SECONDS)
        {
            Log.Info($"[{nameof(SeamothHitch)}] Asking to hold on to the Seamoth {vehicleId}");
            pendingVehicleId = vehicleId;
            pendingSince = Time.unscaledTime;
            packetSender.Send(new PlayerHitched(default, vehicleId));
        }
        return true;
    }

    /// <summary>
    ///     A <see cref="PlayerHitched" /> from the server: this player or a partner holds on now.
    /// </summary>
    public void OnHitched(SessionId playerId, NitroxId vehicleId)
    {
        bool isLocal = playerId == localPlayer.SessionId;
        if (!NitroxEntity.TryGetComponentFrom(vehicleId, out SeaMoth seamoth))
        {
            Log.Warn($"[{nameof(SeamothHitch)}] Session {playerId} holds on to {vehicleId}, which isn't a Seamoth in this game");
            if (isLocal)
            {
                // The server thinks we hold on: tell it we don't
                pendingVehicleId = null;
                packetSender.Send(new PlayerUnhitched(default, Player.main.transform.position.ToDto()));
            }
            return;
        }
        if (isLocal)
        {
            Begin(seamoth, vehicleId);
        }
        else if (playerManager.TryFind(playerId, out RemotePlayer partner))
        {
            partner.SetHitched(seamoth);
        }
    }

    /// <summary>
    ///     A <see cref="PlayerUnhitched" />: a partner let go, or the server ended a hitch (this player's included).
    /// </summary>
    public void OnUnhitched(SessionId playerId, Vector3 position)
    {
        if (playerId == localPlayer.SessionId)
        {
            if (IsLocalPlayerHitched)
            {
                Release("the server ended it", false);
            }
            return;
        }
        if (playerManager.TryFind(playerId, out RemotePlayer partner))
        {
            partner.SetHitched(null, position);
        }
    }

    /// <summary>
    ///     A Seamoth starts docking (in this game: <c>VehicleDockingBay.OnTriggerEnter</c> for the pilot's game, the
    ///     <c>VehicleDocking</c> packet for the others): whoever holds on lets go here and now, before it is pulled into the
    ///     bay. The hitcher's game says where; the server also ends the hitch.
    /// </summary>
    public void OnVehicleDocking(NitroxId vehicleId)
    {
        if (LocalVehicleId == vehicleId)
        {
            Release("the Seamoth docks", true);
        }
        foreach (RemotePlayer partner in playerManager.GetAll())
        {
            if (partner.IsHitched && partner.HitchedVehicle && partner.HitchedVehicle.TryGetNitroxId(out NitroxId id) && id == vehicleId)
            {
                partner.SetHitched(null);
            }
        }
    }

    /// <summary>
    ///     Lets go of the Seamoth, if holding on. <paramref name="send" />: tell the server and the others where.
    /// </summary>
    public void Release(string reason, bool send = true)
    {
        pendingVehicleId = null;
        if (!IsLocalPlayerHitched)
        {
            return;
        }
        Player player = Player.main;
        Vector3 position = player.transform.position;
        Log.Info($"[{nameof(SeamothHitch)}] Letting go of the Seamoth {LocalVehicleId}: {reason}");
        LocalVehicleId = null;
        LastReleaseReason = reason;
        vehicle = null;
        handle = null;

        bool alive = player.liveMixin.IsAlive();
        // A downed player (T1) keeps the input lock RescueWindow put on; a dead one gets its controls back when it respawns
        if (alive && !RescueWindow.IsLocalPlayerDowned)
        {
            Inventory.main.quickSlots.SetIgnoreHotkeyInput(false);
            if (controllerWasEnabled)
            {
                player.playerController.SetEnabled(true);
            }
        }
        if (send)
        {
            packetSender.Send(new PlayerUnhitched(default, position.ToDto()));
        }
    }

    /// <summary>
    ///     Every frame (<see cref="SeamothHitchDriver" />): let go when something ends the ride, else the "let go" line.
    /// </summary>
    public void Update()
    {
        if (!IsLocalPlayerHitched || !Player.main)
        {
            return;
        }
        string? reason = ReleaseReason();
        if (reason != null)
        {
            Release(reason);
            return;
        }
        HandReticle.main.SetText(HandReticle.TextType.Use, "Nitrox_HitchLetGo", true, GameInput.Button.Exit);
    }

    /// <summary>
    ///     After this game's copy of the Seamoth moved (its replicator, or the pilot's physics): the player goes along.
    /// </summary>
    public void LateUpdate()
    {
        if (IsLocalPlayerHitched && handle && Player.main)
        {
            Player.main.transform.position = handle.position;
        }
    }

    /// <summary>
    ///     The handle the hitcher hangs on, created on first use. The same pose in every game, so nothing about it is sent.
    /// </summary>
    public static Transform EnsureHandle(Vehicle seamoth)
    {
        Transform existing = seamoth.transform.Find(HANDLE_NAME);
        if (existing)
        {
            return existing;
        }
        Transform created = new GameObject(HANDLE_NAME).transform;
        created.SetParent(seamoth.transform, false);
        created.localPosition = HandleLocalPosition;
        created.localRotation = Quaternion.identity;
        return created;
    }

    private void Begin(SeaMoth seamoth, NitroxId vehicleId)
    {
        pendingVehicleId = null;
        Player player = Player.main;
        if (IsLocalPlayerHitched || !player.liveMixin.IsAlive() || RescueWindow.IsLocalPlayerDowned || player.GetMode() != Player.Mode.Normal)
        {
            // Too late: we went down, got into a vehicle or hold on already. The server must forget it.
            Log.Info($"[{nameof(SeamothHitch)}] The server let us hold on to {vehicleId}, but we can't now");
            packetSender.Send(new PlayerUnhitched(default, player.transform.position.ToDto()));
            return;
        }
        Log.Info($"[{nameof(SeamothHitch)}] Holding on to the Seamoth {vehicleId}");
        // No tools while holding on (§2.2): the held item goes away, quick slots are ignored, Player.IsFreeToInteract is false
        Inventory.main.ReturnHeld();
        Inventory.main.quickSlots.DeselectImmediate();
        Inventory.main.quickSlots.SetIgnoreHotkeyInput(true);
        // Switching the motor off makes the body kinematic without collisions (UnderwaterMotor.SetEnabled): it can't push
        // the Seamoth or get stuck on terrain while it is carried
        controllerWasEnabled = player.playerController.enabled;
        player.playerController.SetEnabled(false);
        vehicle = seamoth;
        handle = EnsureHandle(seamoth);
        LocalVehicleId = vehicleId;
        noPilotSince = -1;
        player.transform.position = handle.position;
    }

    private string? ReleaseReason()
    {
        Player player = Player.main;
        if (!vehicle || !handle || !vehicle.liveMixin || !vehicle.liveMixin.IsAlive())
        {
            return "the Seamoth is gone";
        }
        if (vehicle.docked)
        {
            return "the Seamoth docked";
        }
        if (!player.liveMixin.IsAlive())
        {
            return "died";
        }
        if (RescueWindow.IsLocalPlayerDowned)
        {
            return "went down";
        }
        if (player.GetMode() != Player.Mode.Normal || player.cinematicModeActive)
        {
            return "in a vehicle or a cinematic";
        }
        if (HasPilot(vehicle))
        {
            noPilotSince = -1;
        }
        else if (noPilotSince < 0)
        {
            noPilotSince = Time.unscaledTime;
        }
        else if (Time.unscaledTime - noPilotSince > NO_PILOT_SECONDS)
        {
            return "nobody pilots the Seamoth";
        }
        if (GameInput.GetButtonDown(GameInput.Button.Exit) && !player.GetPDA().isInUse)
        {
            return "let go";
        }
        return null;
    }

    private bool CanHoldOn(Vehicle target, out NitroxId vehicleId)
    {
        vehicleId = null!;
        return localPlayer.CoopSettings.SeamothHitch &&
               target is SeaMoth &&
               !target.docked &&
               !IsLocalPlayerHitched &&
               !RescueWindow.IsLocalPlayerDowned &&
               Player.main.GetMode() == Player.Mode.Normal &&
               HasPilot(target) &&
               target.TryGetNitroxId(out vehicleId);
    }

    private bool HasPilot(Vehicle target)
    {
        foreach (RemotePlayer partner in playerManager.GetAll())
        {
            if (partner.Vehicle == target)
            {
                return true;
            }
        }
        return false;
    }
}
