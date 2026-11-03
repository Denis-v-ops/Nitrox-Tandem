using System;
using System.Collections;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;
using Story;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     COOP_FEATURES_SPEC G3c: the cure starts once both players have taken a cure ball (<see cref="EnzymeBall" />). A
///     player's click on a ball is held (<c>EnzymeBall_OnHandClick_Patch</c>) and this component, added to that ball,
///     withdraws the ready when the ball is gone or the player swam away from it; on the start each game runs its own
///     ball's cure cinematic. Each game has its own balls (the babies drop them in every game since FIX_SPEC S17), so the
///     click doesn't name a ball the other game knows.
/// </summary>
public class CureBallGate : MonoBehaviour
{
    /// <summary>A player further than this (m) from the ball they took has walked away: the ready is withdrawn.</summary>
    public const float LEAVE_DISTANCE = 10f;

    /// <summary>How long a partner's relayed cure waits for this player's own cure cinematic to end.</summary>
    private const float RELAYED_CURE_WAIT_SECONDS = 60f;

    private const float CHECK_SECONDS = 0.5f;

    /// <summary>The ball whose click is held now; a click on another ball replaces it.</summary>
    private static EnzymeBall heldBall;

    /// <summary>The ball whose cure cinematic this player plays (or played last).</summary>
    private static EnzymeBall curingBall;

    private EnzymeBall ball;
    private StoryGates storyGates;
    private float nextCheck;

    /// <summary>This player's cure cinematic is playing.</summary>
    public static bool IsCuring => curingBall && curingBall.playerCinematicController && curingBall.playerCinematicController.cinematicModeActive;

    /// <summary>Holds the click on <paramref name="ball" /> until the partner took a ball too.</summary>
    public static void Hold(EnzymeBall ball, StoryGates storyGates)
    {
        heldBall = ball;
        storyGates.Hold(StoryGateKind.CURE, () => Resume(ball));
        if (!ball.TryGetComponent(out CureBallGate gate))
        {
            gate = ball.gameObject.AddComponent<CureBallGate>();
        }
        gate.ball = ball;
        gate.storyGates = storyGates;
        gate.nextCheck = 0;
        gate.enabled = true;
    }

    /// <summary>The click goes through: the game starts this ball's cure.</summary>
    public static void OnCureStarts(EnzymeBall ball)
    {
        curingBall = ball;
        if (ReferenceEquals(heldBall, ball))
        {
            heldBall = null;
        }
    }

    /// <summary>
    ///     The partner's cure finished first: the game would remove this player's infection now
    ///     (<c>StoryGoalCustomEventHandler</c>, <c>Infection_Progress5</c>), while this player's own cure cinematic still plays
    ///     the infection fading. That cinematic completes the same goal when it ends, so the relayed one waits for it
    ///     (bounded), and is applied only if the goal still isn't complete then.
    /// </summary>
    /// <returns>True if <paramref name="applyRelayedGoal" /> was deferred.</returns>
    public static bool TryDeferRelayedCure(string goalKey, Action applyRelayedGoal)
    {
        if (!IsCuring || !Player.main || !string.Equals(goalKey, curingBall.onPlayerCuredGoal.key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        Log.Info($"[{nameof(CureBallGate)}] The partner's cure ({goalKey}) waits for this player's own cure cinematic");
        Player.main.StartCoroutine(ApplyAfterOwnCure(goalKey, applyRelayedGoal));
        return true;
    }

    private static IEnumerator ApplyAfterOwnCure(string goalKey, Action applyRelayedGoal)
    {
        float giveUpAt = Time.time + RELAYED_CURE_WAIT_SECONDS;
        while (IsCuring && Time.time < giveUpAt)
        {
            yield return null;
        }
        if (StoryGoalManager.main && StoryGoalManager.main.IsGoalComplete(goalKey))
        {
            yield break;
        }
        Log.Info($"[{nameof(CureBallGate)}] Applying the partner's cure ({goalKey}) after this player's own cure didn't complete it");
        applyRelayedGoal();
    }

    private static void Resume(EnzymeBall ball)
    {
        if (!ball || ball.playerCured)
        {
            // Destroyed meanwhile (an infected creature touched it, or it unloaded), or the goal completed already: this
            // player is cured by the partner's relayed goal, without the cinematic.
            Log.Info($"[{nameof(CureBallGate)}] The held cure ball is gone; the partner's cure will cure this player");
            return;
        }
        ball.OnHandClick();
    }

    private void Update()
    {
        if (Time.time < nextCheck)
        {
            return;
        }
        nextCheck = Time.time + CHECK_SECONDS;
        if (!ReferenceEquals(heldBall, ball) || storyGates == null || !storyGates.IsHeld(StoryGateKind.CURE))
        {
            enabled = false;
            return;
        }
        if (!Player.main || Vector3.Distance(Player.main.transform.position, ball.transform.position) > LEAVE_DISTANCE)
        {
            heldBall = null;
            storyGates.Cancel(StoryGateKind.CURE);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        // The ball was destroyed or unloaded while its click waited
        if (ReferenceEquals(heldBall, ball) && storyGates != null && storyGates.IsHeld(StoryGateKind.CURE))
        {
            heldBall = null;
            storyGates.Cancel(StoryGateKind.CURE);
        }
    }
}
