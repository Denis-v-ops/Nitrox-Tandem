using System;
using System.Collections.Generic;
using System.Linq;

namespace NitroxClient.GameLogic;

/// <summary>
///     The radius of the Aurora's radiation field for a player who joins after the explosion. The radius itself is never
///     sent: every game runs the vanilla <c>LeakingRadiation.Update</c>, which grows it while any leak leaks and shrinks it
///     once none does. Plain data, no game types (MULTINAUGHTICA_SPEC M2).
/// </summary>
public static class RadiationRadiusModel
{
    /// <param name="ApplyRadius">False when nothing changed the radius (before the explosion).</param>
    /// <param name="AllFixed">Every leak is fixed: the game's <c>radiationFixed</c>.</param>
    public readonly record struct Result(bool ApplyRadius, float Radius, bool AllFixed);

    /// <param name="realElapsed">The world's real play time now (server active time).</param>
    /// <param name="realExplosion">The real time at which the Aurora exploded; before it the radius doesn't grow.</param>
    /// <param name="fixTimes">One entry per leak the server sent: the real time it was repaired to full, -1 while it leaks.</param>
    /// <param name="totalLeaks">The leaks of the Aurora (11).</param>
    /// <param name="currentRadius">The radius before this is applied (kept when nothing grew it).</param>
    public static Result Compute(float realElapsed, float realExplosion, IReadOnlyList<float> fixTimes, int totalLeaks, float kStartRadius, float kGrowRate,
                                 float kNaturalDissipation, float kMaxRadius, float currentRadius)
    {
        // The field is repaired only when every leak is: LeakingRadiation.Update grows the radius while ANY leak leaks, and
        // NotifyLeaksFixed sets radiationFixed only when none does. The latest fix time of any leak isn't "all fixed": with
        // it, a joiner got a field that was too small, and a radiationFixed that skipped leaksFixedGoal when they welded
        // the last leak.
        bool allFixed = fixTimes.Count == totalLeaks && fixTimes.All(time => time >= 0f);

        if (!allFixed)
        {
            float elapsedAfterExplosion = realElapsed - realExplosion;
            if (elapsedAfterExplosion <= 0f)
            {
                return new Result(false, currentRadius, false);
            }
            return new Result(true, Clamp(kStartRadius + elapsedAfterExplosion * kGrowRate, 0f, kMaxRadius), false);
        }

        float lastFix = fixTimes.Max();
        float radius = currentRadius;

        // It grew from the explosion until the last leak was fixed (only if that was after the explosion)
        float grownFor = lastFix - realExplosion;
        if (grownFor > 0f)
        {
            radius = Clamp(kStartRadius + grownFor * kGrowRate, 0f, kMaxRadius);
        }

        // and it has shrunk since (kNaturalDissipation is negative); a repair before the explosion counts from the explosion
        float shrunkFor = realElapsed - Math.Max(lastFix, realExplosion);
        if (shrunkFor > 0f)
        {
            radius = Clamp(radius + shrunkFor * kNaturalDissipation, 0f, kMaxRadius);
        }

        return new Result(true, radius, true);
    }

    // Math.Clamp doesn't exist on net472 (the game's runtime)
    private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
}
