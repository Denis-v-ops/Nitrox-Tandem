using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Partner;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     The rescue window on the game side (COOP_FEATURES_SPEC T1, and the partner half of D3). At 0 health the local
///     player goes down instead of dying: health 1, invincible, input locked (the camera stays free), no interactions, a
///     pulsing suffocation-style fade and a countdown. Air keeps draining (no <c>FreezeStats</c>), so drowning still kills.
///     The server decides the window (<c>RescueService</c>); when it ends, the kill goes through and vanilla death and
///     Nitrox's death beacon run unchanged. Partners who are down are tracked here for their marker (D3) and revive action.
/// </summary>
public sealed class RescueWindow
{
    /// <summary>
    ///     The fade pulses between these overlay values, one cycle every <see cref="FADE_PERIOD" /> seconds.
    /// </summary>
    public const float FADE_MIN = 0.35f;

    public const float FADE_MAX = 0.75f;
    public const float FADE_PERIOD = 2.5f;

    private readonly LocalPlayer localPlayer;
    private readonly IPacketSender packetSender;
    private readonly PlayerManager playerManager;
    private readonly Dictionary<SessionId, float> partnerWindowEnds = [];
    private bool controllerWasEnabled;
    private float pendingSince;
    private float windowEndsAt;
    private bool allowingKill;

    public RescueWindow(LocalPlayer localPlayer, IPacketSender packetSender, PlayerManager playerManager)
    {
        this.localPlayer = localPlayer;
        this.packetSender = packetSender;
        this.playerManager = playerManager;
        playerManager.OnRemove += (sessionId, _) => partnerWindowEnds.Remove(sessionId);
        // Static for the patches: a new session starts up
        SetUp();
        LastFade = 0;
    }

    public enum LocalDownedState
    {
        UP,

        /// <summary>Down, waiting for the server's window.</summary>
        WAITING,

        DOWN
    }

    /// <summary>
    ///     The local player's state, static for the patches on hot paths (<c>Player.IsFreeToInteract</c>) and the dev
    ///     bridge.
    /// </summary>
    public static LocalDownedState State { get; private set; }

    public static bool IsLocalPlayerDowned => State != LocalDownedState.UP;

    /// <summary>
    ///     Seconds left in the local player's window; -1 while up or waiting.
    /// </summary>
    public static float SecondsLeft { get; private set; } = -1;

    /// <summary>
    ///     The overlay value the fade set last (for the dev bridge: windowless games draw no screen).
    /// </summary>
    public static float LastFade { get; private set; }

    /// <summary>
    ///     The countdown text shown last.
    /// </summary>
    public static string LastCountdown { get; private set; } = "";

    /// <summary>
    ///     Called by the <c>LiveMixin.Kill</c> prefix for the local player.
    /// </summary>
    /// <returns>True if the player went down instead, and the kill must not run.</returns>
    public bool TryGoDown(DamageType damageType)
    {
        if (allowingKill || !Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted || !localPlayer.SessionId.HasValue)
        {
            return false;
        }
        Player player = Player.main;
        bool subDestroyed = player.currentSub && player.currentSub.subDestroyed;
        DownedKillContext context = new(localPlayer.CoopSettings.RescueWindowSeconds > 0, IsLocalPlayerDowned, player.GetOxygenAvailable(), subDestroyed, damageType);
        if (!DownedRules.GoesDown(context))
        {
            if (IsLocalPlayerDowned)
            {
                // Drowned (or burnt) while down: this kill is the death. The server ends the window for the others.
                Log.Info($"[{nameof(RescueWindow)}] Died while down ({damageType}, oxygen {player.GetOxygenAvailable()})");
                ClearForDeath();
                packetSender.Send(new PlayerRescueExpired(default));
            }
            return false;
        }

        Log.Info($"[{nameof(RescueWindow)}] Down instead of dead ({damageType}); asking the server for a window");
        GoDown();
        State = LocalDownedState.WAITING;
        pendingSince = Time.unscaledTime;
        packetSender.Send(new PlayerDowned(default, 0));
        return true;
    }

    /// <summary>
    ///     A <see cref="PlayerDowned" /> from the server.
    /// </summary>
    public void OnDowned(SessionId playerId, float windowSeconds)
    {
        if (playerId != localPlayer.SessionId)
        {
            partnerWindowEnds[playerId] = Time.unscaledTime + windowSeconds;
            if (playerManager.TryFind(playerId, out RemotePlayer partner))
            {
                SetPartnerDowned(partner, true);
            }
            return;
        }
        if (!Player.main || !Player.main.liveMixin.IsAlive())
        {
            return;
        }
        if (State == LocalDownedState.UP)
        {
            // Rejoined while down: the game came back with the saved health and nothing else
            Log.Info($"[{nameof(RescueWindow)}] Still down after rejoining: {windowSeconds} s left");
            GoDown();
        }
        State = LocalDownedState.DOWN;
        windowEndsAt = Time.unscaledTime + windowSeconds;
    }

    /// <summary>
    ///     A <see cref="PlayerRevived" /> from the server.
    /// </summary>
    public void OnRevived(SessionId playerId, float health)
    {
        if (playerId != localPlayer.SessionId)
        {
            partnerWindowEnds.Remove(playerId);
            if (playerManager.TryFind(playerId, out RemotePlayer partner))
            {
                SetPartnerDowned(partner, false);
            }
            return;
        }
        if (!IsLocalPlayerDowned)
        {
            return;
        }
        Log.Info($"[{nameof(RescueWindow)}] Revived with {health} health");
        Player player = Player.main;
        LiveMixin liveMixin = player.liveMixin;
        liveMixin.invincible = false;
        liveMixin.health = Mathf.Max(liveMixin.health, Mathf.Min(health, liveMixin.maxHealth));
        player.playerController.inputEnabled = true;
        if (controllerWasEnabled)
        {
            player.playerController.SetEnabled(true);
        }
        Inventory.main.quickSlots.SetIgnoreHotkeyInput(false);
        player.GetPDA().SetIgnorePDAInput(false);
        SetUp();
    }

    /// <summary>
    ///     A <see cref="PlayerRescueExpired" /> from the server: the window ran out or was refused.
    /// </summary>
    public void OnExpired(SessionId playerId)
    {
        if (playerId != localPlayer.SessionId)
        {
            // Their own game lets the kill through; the body keeps its pose until their death arrives (PlayerDeathEvent)
            partnerWindowEnds.Remove(playerId);
            if (playerManager.TryFind(playerId, out RemotePlayer partner) && partner.Body && partner.Body.TryGetComponent(out PartnerHandTarget handTarget))
            {
                handTarget.IsDowned = false;
            }
            return;
        }
        if (IsLocalPlayerDowned)
        {
            Die("the server ended the window");
        }
    }

    /// <summary>
    ///     A partner died (their <see cref="PlayerDeathEvent" />).
    /// </summary>
    public void OnPartnerDied(RemotePlayer partner)
    {
        partnerWindowEnds.Remove(partner.SessionId);
        SetPartnerDowned(partner, false);
    }

    /// <summary>
    ///     Seconds left in a downed partner's window, as the server sent it.
    /// </summary>
    public bool TryGetPartnerSecondsLeft(SessionId playerId, out float secondsLeft)
    {
        if (partnerWindowEnds.TryGetValue(playerId, out float endsAt))
        {
            secondsLeft = Mathf.Max(0, endsAt - Time.unscaledTime);
            return true;
        }
        secondsLeft = 0;
        return false;
    }

    /// <summary>
    ///     Every frame (<see cref="RescueWindowHud" />): the countdown, and the local safety nets if the server stays silent.
    /// </summary>
    public void Update()
    {
        if (!IsLocalPlayerDowned || !Player.main)
        {
            return;
        }
        float now = Time.unscaledTime;
        if (State == LocalDownedState.WAITING)
        {
            SecondsLeft = -1;
            if (now - pendingSince > DownedRules.NO_ANSWER_SECONDS)
            {
                packetSender.Send(new PlayerRescueExpired(default));
                Die("no answer from the server");
            }
            return;
        }

        SecondsLeft = Mathf.Max(0, windowEndsAt - now);
        if (now - windowEndsAt > DownedRules.EXPIRY_GRACE_SECONDS)
        {
            packetSender.Send(new PlayerRescueExpired(default));
            Die("the window ended long ago and the server didn't say so");
            return;
        }
        LastCountdown = Language.main.Get("Nitrox_RescueDowned").Replace("{SECONDS}", Mathf.CeilToInt(SecondsLeft).ToString());
        HandReticle.main.SetText(HandReticle.TextType.Use, LastCountdown, false);
    }

    /// <summary>
    ///     After <c>Player.Update</c> wrote the suffocation overlay: a slow pulse on top of it, the real suffocation wins
    ///     when it is darker.
    /// </summary>
    public void LateUpdate()
    {
        if (!IsLocalPlayerDowned || !Player.main || !uGUI.main)
        {
            return;
        }
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / FADE_PERIOD);
        float pulse = Mathf.Lerp(FADE_MIN, FADE_MAX, wave);
        LastFade = Mathf.Max(pulse, 1f - Player.main.suffocationProgress);
        uGUI.main.overlays.Set(uGUI_Overlays.OutOfOxygen, LastFade);
    }

    /// <summary>
    ///     Health 1 (above 0, so <c>Player.SuffocationDie</c>'s <c>IsAlive</c> check still lets drowning kill), invincible
    ///     (<c>Kill</c> ignores it, so the timer and drowning still work), out of any vehicle, input locked as in
    ///     <c>Player.OnKill</c> but with the camera free.
    /// </summary>
    private void GoDown()
    {
        Player player = Player.main;
        LiveMixin liveMixin = player.liveMixin;
        liveMixin.health = 1f;
        liveMixin.tempDamage = 0f;
        liveMixin.invincible = true;

        // Out of the Seamoth or Prawn suit, next to it (as Vehicle.OnKill does), so a partner can reach the body; also off the Cyclops helm
        if (player.GetMode() is Player.Mode.Piloting or Player.Mode.LockedPiloting)
        {
            if (!player.ToNormalMode())
            {
                player.ToNormalMode(false);
                player.transform.parent = null;
            }
        }

        PDA pda = player.GetPDA();
        if (pda.state is PDA.State.Opening or PDA.State.Opened)
        {
            pda.SetIgnorePDAInput(false);
            pda.Close();
        }
        pda.SetIgnorePDAInput(true);
        Inventory.main.quickSlots.SetIgnoreHotkeyInput(true);
        player.playerController.inputEnabled = false;
        controllerWasEnabled = player.playerController.enabled;
        player.playerController.SetEnabled(false);
    }

    /// <summary>
    ///     Lets the kill through: vanilla death (<c>Player.OnKill</c>) locks input itself and its respawn unlocks it.
    /// </summary>
    private void Die(string reason)
    {
        Log.Info($"[{nameof(RescueWindow)}] Dying: {reason}");
        ClearForDeath();
        LiveMixin liveMixin = Player.main.liveMixin;
        if (!liveMixin.IsAlive())
        {
            return;
        }
        allowingKill = true;
        try
        {
            liveMixin.Kill();
        }
        finally
        {
            allowingKill = false;
        }
    }

    private void ClearForDeath()
    {
        Player.main.liveMixin.invincible = false;
        SetUp();
    }

    private void SetUp()
    {
        State = LocalDownedState.UP;
        SecondsLeft = -1;
        LastCountdown = "";
    }

    private static void SetPartnerDowned(RemotePlayer partner, bool downed)
    {
        if (!partner.Body)
        {
            return;
        }
        if (partner.Body.TryGetComponent(out PartnerHandTarget handTarget))
        {
            handTarget.IsDowned = downed;
        }
        partner.SetDownedPose(downed);
    }
}
