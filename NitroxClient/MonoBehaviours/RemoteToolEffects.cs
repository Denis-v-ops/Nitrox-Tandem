using System.Collections.Generic;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Plays the beam, sparks and sound of the tool in the partner's hand while they use it (COOP_FEATURES_SPEC R2c). Each
///     tool plays them only for the player who holds it: its effects run from <c>Update</c>/<c>LateUpdate</c> behind
///     <c>isDrawn</c>, or need <c>usingPlayer</c> and the local input. Driven by the arm states the partner's game sends
///     (R2b) as the body's animator has them, i.e. what <see cref="RemotePlayer.SetArmState" /> wrote, and never while a
///     cinematic holds the body (R1b).
///     <para />
///     Sounds that Nitrox already relays (whitelisted in <c>SoundWhitelist_Subnautica.csv</c>: the laser cutter's) aren't
///     played here; the scanner's, builder's and welder's loops aren't relayed, and playing them here sends nothing.
/// </summary>
public class RemoteToolEffects : MonoBehaviour
{
    private const float LASER_FLICKER_INTERVAL = 0.05f;

    /// <summary>Ghosts other players' games progressed (<c>BuildingHandler.ProgressConstruction</c>), and when.</summary>
    private static readonly Dictionary<Constructable, float> remoteBuildProgress = [];

    /// <summary>What each partner's builder builds this frame, read by <c>BuilderTool_LateUpdate_Patch</c>.</summary>
    private static readonly Dictionary<BuilderTool, Constructable> buildTargets = [];

    private static readonly List<(Constructable Target, float Time, float Distance)> buildCandidates = [];
    private static readonly List<Constructable> staleBuildProgress = [];

    private RemotePlayer remotePlayer;
    private PlayerTool? playingOn;
    private RemoteToolKind playingKind;
    private float laserIntensity;
    private float laserIntensityTarget;
    private float nextLaserFlickerTime;

    /// <summary>For the dev bridge: the tool whose effects play on this body now (<see cref="RemoteToolKind" />).</summary>
    public string Playing => playingOn ? playingKind.ToString() : nameof(RemoteToolKind.NONE);

    /// <summary>For the dev bridge: the ghost this body's builder beams at, if any.</summary>
    public Constructable? BuildTarget => playingOn is BuilderTool builder && buildTargets.TryGetValue(builder, out Constructable target) ? target : null;

    public void Initialize(RemotePlayer player)
    {
        remotePlayer = player;
    }

    /// <summary>
    ///     Another player's game built or took apart some of this ghost (a <c>ModifyConstructedAmount</c> packet): their
    ///     builder beams at it.
    /// </summary>
    public static void NoteRemoteConstruction(Constructable constructable)
    {
        remoteBuildProgress[constructable] = Time.time;
    }

    /// <summary>
    ///     The ghost a partner's builder (the one in their body's hand) builds now. <c>BuilderTool.LateUpdate</c> runs on that
    ///     copy too and turns its beams off every frame unless it has one.
    /// </summary>
    public static bool TryGetBuildTarget(BuilderTool builder, out Constructable target)
    {
        return buildTargets.TryGetValue(builder, out target) && target;
    }

    private void Update()
    {
        if (remotePlayer == null || !remotePlayer.AnimationController)
        {
            return;
        }

        PlayerTool? tool = remotePlayer.ItemAttachPoint ? remotePlayer.ItemAttachPoint.GetComponentInChildren<PlayerTool>() : null;
        RemoteToolKind kind = KindOf(tool);
        AnimationController animator = remotePlayer.AnimationController;
        bool usingTool = animator["using_tool"];
        bool usingToolAlt = animator["using_tool_alt"];
        bool play = RemoteToolEffectRules.ShouldPlay(kind, animator["holding_tool"], usingTool, usingToolAlt, remotePlayer.InCinematic);

        if (playingOn && (!play || playingOn != tool))
        {
            Stop();
        }
        else if (!playingOn && playingKind != RemoteToolKind.NONE)
        {
            // The tool was destroyed while it played
            Stop();
        }
        if (play && !playingOn)
        {
            Begin(tool!, kind);
        }
        if (playingOn)
        {
            Tick(usingTool, usingToolAlt);
        }
    }

    private void OnDisable()
    {
        Stop();
    }

    private static RemoteToolKind KindOf(PlayerTool? tool)
    {
        return tool switch
        {
            ScannerTool => RemoteToolKind.SCANNER,
            BuilderTool => RemoteToolKind.BUILDER,
            Welder => RemoteToolKind.WELDER,
            LaserCutter => RemoteToolKind.LASER_CUTTER,
            _ => RemoteToolKind.NONE
        };
    }

    private void Begin(PlayerTool tool, RemoteToolKind kind)
    {
        playingOn = tool;
        playingKind = kind;
        // The game picks the welder's and laser cutter's effect by the *local* player's Player.IsUnderwater
        int vfxIndex = remotePlayer.AnimationController["is_underwater"] ? 1 : 0;
        switch (tool)
        {
            case ScannerTool scanner:
                // Not ScannerTool.SetFXActive: it also lays the scan overlay on the local player's scan target
                scanner.scanBeam.SetActive(true);
                scanner.scanSound.Play();
                break;
            case Welder welder:
                if (welder.fxControl && MiscSettings.flashes)
                {
                    welder.fxControl.Play(vfxIndex);
                }
                welder.weldSound.Play();
                break;
            case LaserCutter cutter:
                if (cutter.fxControl && MiscSettings.flashes)
                {
                    cutter.fxControl.Play(vfxIndex);
                    laserIntensity = cutter.fxLight.intensity;
                    cutter.fxLight.enabled = true;
                    nextLaserFlickerTime = 0;
                }
                // No sound: its cut_loop is whitelisted, the partner's game already relays it
                break;
        }
    }

    private void Tick(bool usingTool, bool usingToolAlt)
    {
        switch (playingOn)
        {
            case ScannerTool scanner:
                // The scanner's own nozzle animation (ScannerTool.LateUpdate sets it for the local player only)
                if (scanner.animator && scanner.animator.isActiveAndEnabled)
                {
                    SafeAnimator.SetBool(scanner.animator, "using_tool", usingTool);
                    SafeAnimator.SetBool(scanner.animator, "using_tool_alt", usingToolAlt);
                }
                break;
            case BuilderTool builder:
                if (TryFindBuildTarget(out Constructable target))
                {
                    buildTargets[builder] = target;
                }
                else
                {
                    buildTargets.Remove(builder);
                }
                break;
            case LaserCutter cutter when cutter.fxLight.enabled:
                // As LaserCutter.RandomizeIntensity and its Update
                if (Time.time >= nextLaserFlickerTime)
                {
                    laserIntensityTarget = Random.Range(0f, 4f);
                    nextLaserFlickerTime = Time.time + LASER_FLICKER_INTERVAL;
                }
                cutter.fxLight.intensity = Mathf.MoveTowards(cutter.fxLight.intensity, laserIntensityTarget, Time.deltaTime * 25f);
                break;
        }
    }

    private void Stop()
    {
        PlayerTool? tool = playingOn;
        playingOn = null;
        playingKind = RemoteToolKind.NONE;
        if (!tool)
        {
            return;
        }
        switch (tool)
        {
            case ScannerTool scanner:
                scanner.scanBeam.SetActive(false);
                scanner.scanSound.Stop();
                if (scanner.animator && scanner.animator.isActiveAndEnabled)
                {
                    SafeAnimator.SetBool(scanner.animator, "using_tool", false);
                    SafeAnimator.SetBool(scanner.animator, "using_tool_alt", false);
                }
                break;
            case BuilderTool builder:
                // Its own LateUpdate then turns the beams and the sound off
                buildTargets.Remove(builder);
                break;
            case Welder welder:
                if (welder.fxControl)
                {
                    welder.fxControl.StopAndDestroy(0f);
                }
                welder.weldSound.Stop();
                break;
            case LaserCutter cutter:
                if (cutter.fxControl)
                {
                    cutter.fxControl.StopAndDestroy(0f);
                }
                cutter.fxLight.enabled = false;
                cutter.fxLight.intensity = laserIntensity;
                break;
        }
    }

    private bool TryFindBuildTarget(out Constructable target)
    {
        float now = Time.time;
        Vector3 position = transform.position;
        buildCandidates.Clear();
        staleBuildProgress.Clear();
        foreach (KeyValuePair<Constructable, float> progress in remoteBuildProgress)
        {
            if (!progress.Key || now - progress.Value > RemoteToolEffectRules.BUILD_TARGET_MAX_AGE)
            {
                staleBuildProgress.Add(progress.Key);
                continue;
            }
            if (!progress.Key.constructed)
            {
                buildCandidates.Add((progress.Key, progress.Value, Vector3.Distance(position, progress.Key.transform.position)));
            }
        }
        foreach (Constructable stale in staleBuildProgress)
        {
            remoteBuildProgress.Remove(stale);
        }
        bool found = RemoteToolEffectRules.TryPickBuildTarget(buildCandidates, now, out Constructable? picked);
        target = picked!;
        return found;
    }
}
