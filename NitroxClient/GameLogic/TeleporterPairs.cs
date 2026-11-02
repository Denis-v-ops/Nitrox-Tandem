using Nitrox.Model.Subnautica.Helper;
using NitroxClient.MonoBehaviours;
using Story;

namespace NitroxClient.GameLogic;

/// <summary>
///     Shares which Precursor teleporter pairs are active (<see cref="TeleporterGoalKey" />, MULTINAUGHTICA_SPEC M2): a pair one
///     player activates is active for everyone, also for a player who loads the far end later, joins later or plays after a
///     restart.
/// </summary>
public static class TeleporterPairs
{
    /// <summary>A pair was activated in this game (<c>TeleporterManager.ActivateTeleporter</c>).</summary>
    public static void ReportActivation(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || !Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted || !StoryGoalManager.main)
        {
            return;
        }
        // Sent by StoryGoalManager_OnGoalComplete_Patch the first time only
        StoryGoalManager.main.OnGoalComplete(TeleporterGoalKey.Format(identifier));
    }

    /// <summary>A goal was completed in this game, by this player or the partner: switches the pair on if it's a pair goal.</summary>
    public static void OnGoalCompleted(string goalKey)
    {
        if (!TeleporterGoalKey.TryParse(goalKey, out string identifier) || !TeleporterManager.main || TeleporterManager.GetTeleporterActive(identifier))
        {
            return;
        }
        // Adds it to the active set and opens the pair's loaded teleporters. Its report (ReportActivation) finds the goal complete.
        TeleporterManager.ActivateTeleporter(identifier);
    }

    /// <summary>
    ///     Before a teleporter's <c>Start</c> reads <c>TeleporterManager.GetTeleporterActive</c>: a pair whose goal is complete
    ///     (it came with the join) goes into the game's set, so the teleporter starts open.
    /// </summary>
    public static void RestoreFromGoal(string identifier)
    {
        if (!string.IsNullOrEmpty(identifier) && TeleporterManager.main && StoryGoalManager.main && StoryGoalManager.main.IsGoalComplete(TeleporterGoalKey.Format(identifier)))
        {
            TeleporterManager.main.activeTeleporters.Add(identifier);
        }
    }
}
