namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M4: when the simulating game sends a creature's action (<see cref="AI.BroadcastNewAction" />).
///     <c>Creature.ChooseBestAction</c> runs on every AI update, so it sends only changes, plus a refresh for players who
///     loaded the creature after the last change. Free of Unity types so it can be tested.
/// </summary>
public static class CreatureActionBroadcastRules
{
    /// <summary>
    ///     Seconds after which an unchanged action is sent again.
    /// </summary>
    public const float REFRESH_INTERVAL = 5f;

    /// <summary>
    ///     The name sent for "no action". <c>Creature.ChooseBestAction</c> (game 83031) returns null when the creature has no
    ///     enabled action, or none was running and none evaluates above 0. The other games then clear the action too.
    /// </summary>
    public const string NO_ACTION = "";

    /// <param name="lastSentAction">The action name sent last for this creature, or null if none was sent.</param>
    /// <param name="lastSentTime">When it was sent (seconds), or null if nothing was sent.</param>
    /// <param name="action">The action name the creature chose now (<see cref="NO_ACTION" /> for none).</param>
    /// <param name="now">The time now, on the same clock as <paramref name="lastSentTime" />.</param>
    public static bool ShouldSend(string lastSentAction, float? lastSentTime, string action, float now)
    {
        if (lastSentTime == null || lastSentAction != action)
        {
            return true;
        }
        // Negated so that a broken clock (NaN) still refreshes
        return !(now - lastSentTime.Value < REFRESH_INTERVAL);
    }
}
