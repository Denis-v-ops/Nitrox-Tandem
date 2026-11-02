using NitroxClient.GameLogic;
using UnityEngine;
using static Nitrox.Model.Subnautica.Packets.PlayerPainSound;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     A downed partner's body (COOP_FEATURES_SPEC T1): it collapses with the game's <c>player_death</c> animation and
///     stays down, and a weak pain sound comes from it every few seconds so a rescuer can follow it in the dark (D1).
/// </summary>
/// <remarks>
///     The death clips play once (about 1.2 s) and the animator's "Death" layer then goes back to its empty state (A0
///     probe), so the trigger alone gives a collapse, not a lying body. The pose is held by stopping the animator near
///     the clip's end; if the clip was already left, it is played again at that point first. Revived: the animator runs
///     on, the clip ends and the body gets up.
/// </remarks>
public class DownedPose : MonoBehaviour
{
    public const string DEATH_LAYER = "Death";
    public const string DEATH_TRIGGER = "player_death";

    /// <summary>
    ///     Where in the death clip (normalized time) the pose is held.
    /// </summary>
    public const float HOLD_AT = 0.9f;

    public const float SOUND_INTERVAL = 4f;

    private RemotePlayer remotePlayer;
    private Animator animator;
    private int layer = -1;
    private int deathStateHash;
    private float nextSoundTime;

    /// <summary>
    ///     The animator is stopped in the collapsed pose (for the dev bridge).
    /// </summary>
    public bool Held { get; private set; }

    public bool Active => enabled;

    public void Begin(RemotePlayer player)
    {
        remotePlayer = player;
        animator = player.PlayerModel.GetComponent<Animator>();
        layer = animator.GetLayerIndex(DEATH_LAYER);
        animator.speed = 1f;
        animator.SetTrigger(DEATH_TRIGGER);
        Held = false;
        deathStateHash = 0;
        nextSoundTime = Time.time + 1f;
        enabled = true;
    }

    public void End()
    {
        if (animator)
        {
            animator.ResetTrigger(DEATH_TRIGGER);
            animator.speed = 1f;
        }
        Held = false;
        enabled = false;
    }

    public void Update()
    {
        if (!animator)
        {
            return;
        }
        if (!Held && layer >= 0)
        {
            HoldWhenCollapsed();
        }
        if (Time.time >= nextSoundTime)
        {
            nextSoundTime = Time.time + SOUND_INTERVAL;
            if (remotePlayer.Body && remotePlayer.Body.TryGetComponent(out RemotePlayerSounds sounds))
            {
                sounds.PlayPain(PainKind.WITHOUT_TANK_LIGHT);
            }
        }
    }

    public void OnDisable()
    {
        if (animator)
        {
            animator.speed = 1f;
        }
    }

    private void HoldWhenCollapsed()
    {
        bool inClip = animator.GetCurrentAnimatorClipInfoCount(layer) > 0 && !animator.IsInTransition(layer);
        if (inClip)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
            deathStateHash = state.fullPathHash;
            if (state.normalizedTime >= HOLD_AT)
            {
                Hold();
            }
        }
        else if (deathStateHash != 0)
        {
            // The clip ended between two frames: back to its end, then hold
            animator.Play(deathStateHash, layer, HOLD_AT);
            Hold();
        }
    }

    private void Hold()
    {
        animator.speed = 0f;
        Held = true;
    }
}
