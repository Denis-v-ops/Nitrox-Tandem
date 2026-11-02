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
    ///     True if a goal the partner executed should complete here without its PDA log entry and voice line.
    /// </summary>
    public static bool CompletesSilently(string key, StoryGoalExecuted.EventType type, bool partnerDiscoveriesSilent, ISet<string> experientialKeys)
    {
        return partnerDiscoveriesSilent && type == StoryGoalExecuted.EventType.PDA && experientialKeys.Contains(key);
    }
}
