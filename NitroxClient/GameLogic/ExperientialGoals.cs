using System;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.Packets;
using Story;

namespace NitroxClient.GameLogic;

/// <summary>
///     D5a "your own firsts": the PDA's biome and location lines belong to whoever gets there. These goals are the ones
///     in the game's <see cref="BiomeGoalData" /> and <see cref="LocationGoalData" />.
/// </summary>
/// <remarks>
///     When the partner gets there first, this game completes the goal without <see cref="PDALog.Add" />. Its own
///     tracker still fires later: <c>BiomeGoal.Trigger</c>/<c>LocationGoal.Trigger</c> don't check completion,
///     <see cref="StoryGoal.Execute" /> calls <see cref="PDALog.Add" /> whatever <c>OnGoalComplete</c> returns, and
///     <see cref="PDALog.Add" /> only stays silent when the entry already exists.
/// </remarks>
public static class ExperientialGoals
{
    private static HashSet<string>? keys;

    /// <summary>
    ///     The experiential goal keys, read once from the trackers' <c>goalData</c> (the assets, not the trackers'
    ///     <c>goals</c> lists, which shrink as goals trigger and on initial sync).
    /// </summary>
    public static ISet<string> Keys
    {
        get
        {
            if (keys != null)
            {
                return keys;
            }
            StoryGoalManager manager = StoryGoalManager.main;
            if (!manager)
            {
                return new HashSet<string>();
            }
            BiomeGoalData biomeData = manager.biomeGoalTracker.AliveOrNull()?.goalData.AliveOrNull();
            LocationGoalData locationData = manager.locationGoalTracker.AliveOrNull()?.goalData.AliveOrNull();
            if (!biomeData || !locationData)
            {
                Log.Warn("[D5a] The biome or location goal data isn't loaded; partner discoveries play as usual");
                return new HashSet<string>();
            }
            keys = CollectKeys(biomeData.goals, locationData.goals);
            Log.Info($"[D5a] {keys.Count} biome and location goals are experiential");
            return keys;
        }
    }

    public static HashSet<string> CollectKeys(params IEnumerable<StoryGoal>?[] goalLists)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase); // like StoryGoalManager.completedGoals
        foreach (IEnumerable<StoryGoal>? goals in goalLists)
        {
            if (goals == null)
            {
                continue;
            }
            foreach (StoryGoal goal in goals)
            {
                if (!string.IsNullOrEmpty(goal?.key))
                {
                    result.Add(goal!.key);
                }
            }
        }
        return result;
    }

    /// <summary>
    ///     True for a biome or location PDA line, whatever the option. This game flags its own executes of them, so the
    ///     server can keep them as heard by this player (D5b).
    /// </summary>
    public static bool IsExperiential(string key, bool isPdaGoal, ISet<string> experientialKeys)
    {
        return isPdaGoal && experientialKeys.Contains(key);
    }

    /// <summary>
    ///     True for a biome or location PDA line while the option is on: each player hears it from their own trigger only.
    /// </summary>
    public static bool IsOwnFirst(string key, bool isPdaGoal, bool partnerDiscoveriesSilent, ISet<string> experientialKeys)
    {
        return partnerDiscoveriesSilent && IsExperiential(key, isPdaGoal, experientialKeys);
    }

    /// <summary>
    ///     True if a PDA the partner found in the world should reach this game's PDA log without its voice line (D5).
    /// </summary>
    public static bool FoundArrivesSilently(bool found, StoryGoalExecuted.EventType type, bool partnerDiscoveriesSilent)
    {
        return partnerDiscoveriesSilent && found && type == StoryGoalExecuted.EventType.PDA;
    }

    /// <summary>
    ///     D5b, on join: true if a completed tracker goal stays in this game's biome or location tracker, so this player's
    ///     own visit can still play it. That is a biome or location PDA line that isn't in the PDA log the server sent
    ///     (the server leaves out the ones this player hasn't heard). Everything else completed is removed, as before.
    /// </summary>
    public static bool StaysInTracker(string key, bool isPdaGoal, bool partnerDiscoveriesSilent, ISet<string> experientialKeys, ICollection<string> completedGoals, ISet<string> pdaLogKeys)
    {
        return !completedGoals.Contains(key) || OwnTriggerPlays(key, isPdaGoal, partnerDiscoveriesSilent, experientialKeys, pdaLogKeys.Contains(key));
    }

    /// <summary>
    ///     True if a goal the partner executed should complete here without its PDA log entry and voice line.
    /// </summary>
    public static bool CompletesSilently(string key, StoryGoalExecuted.EventType type, bool partnerDiscoveriesSilent, ISet<string> experientialKeys)
    {
        return IsOwnFirst(key, type == StoryGoalExecuted.EventType.PDA, partnerDiscoveriesSilent, experientialKeys);
    }

    /// <summary>
    ///     True if this player's own tracker may schedule a goal although it's already complete: the partner completed it
    ///     silently and this player hasn't heard it (no PDA log entry). Nitrox otherwise refuses to schedule completed
    ///     goals (<c>StoryGoalScheduler_Schedule_Patch</c>).
    /// </summary>
    public static bool OwnTriggerPlays(string key, bool isPdaGoal, bool partnerDiscoveriesSilent, ISet<string> experientialKeys, bool inPdaLog)
    {
        return !inPdaLog && IsOwnFirst(key, isPdaGoal, partnerDiscoveriesSilent, experientialKeys);
    }

    /// <summary>
    ///     <see cref="OwnTriggerPlays(string, bool, bool, ISet{string}, bool)" /> for this game.
    /// </summary>
    public static bool OwnTriggerPlays(StoryGoal goal, LocalPlayer localPlayer)
    {
        return OwnTriggerPlays(goal.key, goal.goalType == Story.GoalType.PDA, localPlayer.CoopSettings is { PartnerDiscoveriesSilent: true }, Keys, PDALog.Contains(goal.key));
    }
}
