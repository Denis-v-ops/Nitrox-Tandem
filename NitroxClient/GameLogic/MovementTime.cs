namespace NitroxClient.GameLogic;

/// <summary>
///     The arithmetic on movement snapshot times (<see cref="NitroxClient.MonoBehaviours.MovementBroadcaster" />,
///     <see cref="NitroxClient.MonoBehaviours.MovementReplicator" />). No Unity types, so it can be tested without the game.
/// </summary>
/// <remarks>
///     Snapshots are stamped with <see cref="TimeManager.RealTimeElapsed" />: the server's active play time, which is saved
///     with the world, so it grows with the world's age. A float has a 24-bit mantissa: its step is 3.9 ms from 2^15 s
///     (about 9 h of play), 7.8 ms from 2^16 s (about 18 h) and 15.6 ms from 2^17 s (about 36 h). As floats, every stamp
///     and every frame's "now" was rounded to that step, so remote vehicles stalled and skipped more as the world aged
///     (MULTINAUGHTICA_SPEC M6). Times stay <c>double</c>; only the difference of two times, which is small, becomes a
///     float, and it is taken between the doubles first.
/// </remarks>
public static class MovementTime
{
    /// <summary>
    ///     Seconds from <paramref name="earlier" /> to <paramref name="now" /> (negative when <paramref name="now" /> is
    ///     before it).
    /// </summary>
    public static float Age(double now, double earlier)
    {
        return (float)(now - earlier);
    }

    /// <summary>
    ///     How far <paramref name="now" /> is from <paramref name="previousTime" /> (0) to <paramref name="nextTime" /> (1).
    ///     Not clamped.
    /// </summary>
    public static float InterpolationFactor(double now, double previousTime, double nextTime)
    {
        return (float)((now - previousTime) / (nextTime - previousTime));
    }
}
