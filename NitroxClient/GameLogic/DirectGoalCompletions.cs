using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic;

/// <summary>
///     Goals the game completes with <c>StoryGoalManager.OnGoalComplete</c> directly, outside <c>StoryGoal.Execute</c> (which
///     <c>StoryGoal_Execute_Patch</c> reports), were never sent (MULTINAUGHTICA_SPEC M2). In the game: a Precursor key's
///     <c>PrecursorGlobalKeyActivator.doorActivationKey</c> (doors elsewhere open from it, also after a load), the played
///     radio message's "OnPlay" goal (<c>StoryGoalManager.ExecutePendingRadioMessage</c>), two Sunbeam console commands,
///     the intro's PDA triggers; in Nitrox: the teleporter pair goals (<see cref="TeleporterPairs" />).
///     <c>StoryGoalManager_OnGoalComplete_Patch</c> sends them as Story goals.
/// </summary>
public static class DirectGoalCompletions
{
    /// <summary>
    ///     The intro's triggers (<c>IntroLifepodDirector</c>): each player has their own intro, and a partner's completed
    ///     trigger would be complete before a new player's intro reaches it, so what hangs on it wouldn't run for them.
    /// </summary>
    private static readonly HashSet<string> ownIntroGoals = new(StringComparer.OrdinalIgnoreCase) { "Trigger_PDAIntroBegin", "Trigger_PDAIntroEnd" };

    /// <summary>Above 0 while <c>StoryGoal.Execute</c> runs (<c>StoryGoal_Execute_Depth_Patch</c>): that goal is reported there.</summary>
    public static int ExecuteDepth { get; set; }

    /// <param name="key">The goal.</param>
    /// <param name="newlyCompleted">What <c>OnGoalComplete</c> returned: false if the goal was complete already.</param>
    /// <param name="insideExecute">The call comes from <c>StoryGoal.Execute</c>.</param>
    /// <param name="initialSyncCompleted">Before it, goals come from the server, not from this player.</param>
    public static bool ShouldReport(string key, bool newlyCompleted, bool insideExecute, bool initialSyncCompleted)
    {
        return newlyCompleted && !insideExecute && initialSyncCompleted && !string.IsNullOrEmpty(key) && !ownIntroGoals.Contains(key);
    }
}
