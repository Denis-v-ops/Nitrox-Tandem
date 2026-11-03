using System.Collections.Generic;

namespace NitroxClient.GameLogic.PlayerLogic;

/// <summary>
///     The tools whose beam, sparks or sound the partner's body shows (COOP_FEATURES_SPEC R2c).
/// </summary>
public enum RemoteToolKind
{
    NONE,
    SCANNER,
    BUILDER,
    WELDER,
    LASER_CUTTER
}

/// <summary>
///     When the tool in the partner's hand shows its effects (COOP_FEATURES_SPEC R2c), from the arm states their game sends
///     (R2b). The game plays them only for the player who holds the tool (<c>isDrawn</c>/<c>usingPlayer</c>).
/// </summary>
public static class RemoteToolEffectRules
{
    /// <summary>
    ///     A ghost the partner's game progressed longer ago than this isn't what they are building now. Their game sends the
    ///     progress every 0.1 s while they build (throttled <c>ModifyConstructedAmount</c>).
    /// </summary>
    public const float BUILD_TARGET_MAX_AGE = 1f;

    /// <summary>
    ///     The builder's reach (<c>BuilderTool.hitRange</c>).
    /// </summary>
    public const float BUILD_TARGET_RANGE = 30f;

    /// <param name="kind">The tool in the partner body's hand.</param>
    /// <param name="holdingTool">The partner's <c>holding_tool</c>.</param>
    /// <param name="usingTool">The partner's <c>using_tool</c>.</param>
    /// <param name="usingToolAlt">The partner's <c>using_tool_alt</c> (the scanner's self-scan).</param>
    /// <param name="inCinematic">
    ///     A cinematic holds the body (R1b). <c>RemotePlayer.SetArmState</c> keeps the partner's own states back then, so the
    ///     animator can still show a <c>using_tool</c> from before the cinematic.
    /// </param>
    public static bool ShouldPlay(RemoteToolKind kind, bool holdingTool, bool usingTool, bool usingToolAlt, bool inCinematic)
    {
        if (kind == RemoteToolKind.NONE || !holdingTool || inCinematic)
        {
            return false;
        }
        return kind == RemoteToolKind.SCANNER ? usingTool || usingToolAlt : usingTool;
    }

    /// <summary>
    ///     The ghost the partner is building: the one their game progressed most recently, if that was within
    ///     <see cref="BUILD_TARGET_MAX_AGE" /> and it is in the builder's reach of their body.
    /// </summary>
    /// <param name="candidates">Ghosts progressed by other players' games: when (seconds) and how far from the body.</param>
    public static bool TryPickBuildTarget<T>(IEnumerable<(T Target, float Time, float Distance)> candidates, float now, out T? target)
    {
        target = default;
        float latest = float.NegativeInfinity;
        foreach ((T candidate, float time, float distance) in candidates)
        {
            if (now - time > BUILD_TARGET_MAX_AGE || distance > BUILD_TARGET_RANGE || time <= latest)
            {
                continue;
            }
            latest = time;
            target = candidate;
        }
        return !float.IsNegativeInfinity(latest);
    }
}
