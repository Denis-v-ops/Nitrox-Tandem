using System.Collections.Generic;
using System.Linq;
using FMOD.Studio;
using FMODUnity;
using Nitrox.Model.GameLogic.FMOD;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.FMOD;
using UnityEngine;
using static Nitrox.Model.Subnautica.Packets.PlayerPainSound;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     The partner's own body sounds on their body (COOP_FEATURES_SPEC D1): pain when they get hurt, and their breathing
///     loop while they dive with a tank. The event paths come from the local player's emitters, so nothing is hard-coded.
/// </summary>
public class RemotePlayerSounds : MonoBehaviour
{
    /// <summary>
    ///     Pain is silent beyond this.
    /// </summary>
    public const float PAIN_RADIUS = 30f;

    private const float BREATHING_INTERVAL = 0.25f;
    private const float DEFAULT_BREATHING_RADIUS = 20f;

    private static readonly TechType[] tanks = [TechType.Tank, TechType.DoubleTank, TechType.PlasteelTank, TechType.HighCapacityTank];

    private static Dictionary<PainKind, string>? painPaths;
    private static string? breathingPath;

    private readonly Dictionary<PainKind, EventInstance> painEvents = new();
    private RemotePlayer remotePlayer;
    private float breathingRadius = DEFAULT_BREATHING_RADIUS;
    private EventInstance breathing;
    private bool breathingIs3D;
    private PARAMETER_ID depthParameter = FMODUWE.invalidParameterId;
    private PARAMETER_ID rebreatherParameter = FMODUWE.invalidParameterId;
    private float nextBreathingCheckTime;

    /// <summary>
    ///     For the dev bridge: whether the partner breathes from a tank now, and whether the loop is playing here.
    /// </summary>
    public bool Breathes { get; private set; }

    public bool BreathingPlaying { get; private set; }

    public void Initialize(RemotePlayer player, FMODWhitelist whitelist)
    {
        remotePlayer = player;
        ReadPathsFromLocalPlayer();
        if (breathingPath != null && whitelist.IsWhitelisted(breathingPath, out float radius))
        {
            breathingRadius = radius;
        }
    }

    /// <summary>
    ///     Plays a pain sound on the partner's body, as their game did (also the hook for the T1 "downed" sound). Like the
    ///     game, a kind that is still playing isn't restarted.
    /// </summary>
    public void PlayPain(PainKind kind)
    {
        if (painPaths == null || !painPaths.TryGetValue(kind, out string path) || !Player.main)
        {
            return;
        }
        float distance = Vector3.Distance(transform.position, Player.main.transform.position);
        if (distance > PAIN_RADIUS)
        {
            return;
        }

        if (!painEvents.TryGetValue(kind, out EventInstance evt) || !evt.isValid())
        {
            evt = FMODUWE.GetEventImpl(path);
            if (!evt.isValid())
            {
                return;
            }
            painEvents[kind] = evt;
        }
        evt.getPlaybackState(out PLAYBACK_STATE state);
        if (state != PLAYBACK_STATE.STOPPED)
        {
            return;
        }

        PlaceAtBody(evt, PAIN_RADIUS);
        evt.start();
#if DEBUG
        Debuggers.Bridge.BridgeSounds.Record($"{nameof(RemotePlayerSounds)}.{nameof(PlayPain)}", path, transform.position, gameObject);
#endif
    }

    public void Update()
    {
        if (BreathingPlaying)
        {
            // Follows the body; a 2D event gets its volume by distance instead
            PlaceAtBody(breathing, breathingRadius);
        }
        if (Time.unscaledTime < nextBreathingCheckTime)
        {
            return;
        }
        nextBreathingCheckTime = Time.unscaledTime + BREATHING_INTERVAL;
        UpdateBreathing();
    }

    /// <summary>
    ///     Like the game's <c>BreathingSound.UpdateSound</c> for the local player: a tank, swimming, not in a vehicle. The
    ///     partner's game doesn't send it (it starts the loop with <c>FMOD_StudioEventEmitter.StartEvent</c>, which isn't
    ///     synced), so it follows their underwater state and equipment here.
    /// </summary>
    private void UpdateBreathing()
    {
        Breathes = remotePlayer != null && remotePlayer.Body && remotePlayer.AnimationController && remotePlayer.AnimationController["is_underwater"] &&
                   !remotePlayer.Vehicle && !remotePlayer.PilotingChair && HasEquipped(tanks);
        bool audible = Breathes && Player.main && Vector3.Distance(transform.position, Player.main.transform.position) <= breathingRadius;

        if (audible && !BreathingPlaying)
        {
            StartBreathing();
        }
        else if (!audible && BreathingPlaying)
        {
            breathing.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            BreathingPlaying = false;
        }

        if (BreathingPlaying)
        {
            if (FMODUWE.IsValidParameterId(depthParameter))
            {
                breathing.setParameterValueByIndex(depthParameter, transform.position.y);
            }
            if (FMODUWE.IsValidParameterId(rebreatherParameter))
            {
                breathing.setParameterValueByIndex(rebreatherParameter, HasEquipped(TechType.Rebreather) ? 1f : 0f);
            }
        }
    }

    private void StartBreathing()
    {
        if (breathingPath == null)
        {
            return;
        }
        if (!breathing.isValid())
        {
            breathing = FMODUWE.GetEventImpl(breathingPath);
            if (!breathing.isValid())
            {
                return;
            }
            breathing.getDescription(out EventDescription description);
            description.is3D(out breathingIs3D);
            depthParameter = FMODUWE.GetEventInstanceParameterIndex(breathing, "depth");
            rebreatherParameter = FMODUWE.GetEventInstanceParameterIndex(breathing, "rebreather");
        }
        PlaceAtBody(breathing, breathingRadius);
        breathing.start();
        BreathingPlaying = true;
#if DEBUG
        Debuggers.Bridge.BridgeSounds.Record($"{nameof(RemotePlayerSounds)}.{nameof(StartBreathing)}", breathingPath, transform.position, gameObject);
#endif
    }

    private void PlaceAtBody(EventInstance evt, float radius)
    {
        evt.getDescription(out EventDescription description);
        description.is3D(out bool is3D);
        if (is3D)
        {
            evt.set3DAttributes(transform.To3DAttributes());
            evt.setProperty(EVENT_PROPERTY.MINIMUM_DISTANCE, 1f);
            evt.setProperty(EVENT_PROPERTY.MAXIMUM_DISTANCE, radius);
        }
        else if (Player.main)
        {
            evt.setVolume(FMODSystem.CalculateVolume(transform.position, Player.main.transform.position, radius, 1f));
        }
    }

    private bool HasEquipped(params TechType[] techTypes)
    {
        foreach (TechType techType in techTypes)
        {
            if (remotePlayer.Equipment.Contains(techType))
            {
                return true;
            }
        }
        return false;
    }

    public void OnDestroy()
    {
        foreach (EventInstance evt in painEvents.Values)
        {
            if (evt.isValid())
            {
                evt.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                evt.release();
            }
        }
        painEvents.Clear();
        if (breathing.isValid())
        {
            breathing.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            breathing.release();
        }
    }

    private static void ReadPathsFromLocalPlayer()
    {
        if (painPaths != null || !Player.main)
        {
            return;
        }
        PlayerDamageSounds damageSounds = Player.main.GetComponentInChildren<PlayerDamageSounds>(true);
        BreathingSound breathingSound = Player.main.GetComponentInChildren<BreathingSound>(true);
        if (!damageSounds || !breathingSound)
        {
            Log.Error($"[{nameof(RemotePlayerSounds)}] The local player has no {nameof(PlayerDamageSounds)} or {nameof(BreathingSound)}; the partner's pain and breathing stay silent");
            return;
        }
        painPaths = new Dictionary<PainKind, string>
        {
            [PainKind.WITH_TANK] = damageSounds.painWithTank.asset.path,
            [PainKind.WITHOUT_TANK] = damageSounds.painWithoutTank.asset.path,
            [PainKind.WITHOUT_TANK_LIGHT] = damageSounds.painWithoutTankLight.Event,
            [PainKind.WITHOUT_MASK] = damageSounds.painWithoutMask.asset.path,
            [PainKind.SMOKE] = damageSounds.painSmoke.asset.path
        };
        breathingPath = breathingSound.loopingBreathingSound.asset.path;
        Log.Info($"[{nameof(RemotePlayerSounds)}] Partner sounds: {string.Join(", ", painPaths.Values)}, {breathingPath}");
    }
}
