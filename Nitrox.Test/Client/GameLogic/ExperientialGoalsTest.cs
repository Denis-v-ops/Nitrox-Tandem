using System;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.Packets;
using Story;

namespace NitroxClient.GameLogic;

[TestClass]
public class ExperientialGoalsTest
{
    private static readonly HashSet<string> keys = ExperientialGoals.CollectKeys(
        [new BiomeGoal { key = "Goal_BiomeKelpForest", goalType = Story.GoalType.PDA, biome = "kelpForest" }],
        [new LocationGoal { key = "Goal_LocationAuroraEntry", goalType = Story.GoalType.PDA }]);

    [TestMethod]
    public void BiomeAndLocationGoalsAreExperiential()
    {
        keys.Should().BeEquivalentTo("Goal_BiomeKelpForest", "Goal_LocationAuroraEntry");
    }

    [TestMethod]
    public void KeysIgnoreCaseLikeCompletedGoals()
    {
        keys.Contains("goal_biomekelpforest").Should().BeTrue();
    }

    [TestMethod]
    public void MissingDataAndEmptyKeysAreSkipped()
    {
        ExperientialGoals.CollectKeys(null, [new BiomeGoal { key = "" }, null!]).Should().BeEmpty();
    }

    [TestMethod]
    public void PartnersBiomeLineCompletesSilently()
    {
        ExperientialGoals.CompletesSilently("Goal_BiomeKelpForest", StoryGoalExecuted.EventType.PDA, true, keys).Should().BeTrue();
        ExperientialGoals.CompletesSilently("Goal_LocationAuroraEntry", StoryGoalExecuted.EventType.PDA, true, keys).Should().BeTrue();
    }

    [TestMethod]
    public void OptionOffKeepsTodaysBehaviour()
    {
        ExperientialGoals.CompletesSilently("Goal_BiomeKelpForest", StoryGoalExecuted.EventType.PDA, false, keys).Should().BeFalse();
    }

    [TestMethod]
    public void OwnTriggerPlaysUntilHeard()
    {
        // The partner completed it silently: this player's tracker may still schedule it.
        ExperientialGoals.OwnTriggerPlays("Goal_BiomeKelpForest", true, true, keys, inPdaLog: false).Should().BeTrue();
        // Already heard (in the PDA log): Nitrox's "completed goals don't schedule" rule applies as today.
        ExperientialGoals.OwnTriggerPlays("Goal_BiomeKelpForest", true, true, keys, inPdaLog: true).Should().BeFalse();
        ExperientialGoals.OwnTriggerPlays("Goal_BiomeKelpForest", true, false, keys, inPdaLog: false).Should().BeFalse();
        ExperientialGoals.OwnTriggerPlays("Goal_BiomeKelpForest", false, true, keys, inPdaLog: false).Should().BeFalse();
        ExperientialGoals.OwnTriggerPlays("Story_AuroraWarning1", true, true, keys, inPdaLog: false).Should().BeFalse();
    }

    [TestMethod]
    public void OtherGoalsSyncAsToday()
    {
        // A story PDA line (not from the biome or location data), and experiential keys of other types.
        ExperientialGoals.CompletesSilently("Story_AuroraWarning1", StoryGoalExecuted.EventType.PDA, true, keys).Should().BeFalse();
        ExperientialGoals.CompletesSilently("Goal_BiomeKelpForest", StoryGoalExecuted.EventType.STORY, true, keys).Should().BeFalse();
        ExperientialGoals.CompletesSilently("Goal_BiomeKelpForest", StoryGoalExecuted.EventType.RADIO, true, keys).Should().BeFalse();
    }

    [TestMethod]
    public void OwnExecutesAreFlaggedWhateverTheOption()
    {
        ExperientialGoals.IsExperiential("Goal_BiomeKelpForest", true, keys).Should().BeTrue();
        ExperientialGoals.IsExperiential("Goal_BiomeKelpForest", false, keys).Should().BeFalse();
        ExperientialGoals.IsExperiential("Story_AuroraWarning1", true, keys).Should().BeFalse();
    }

    [TestMethod]
    public void FoundLogsArriveSilentlyWhileTheOptionIsOn()
    {
        ExperientialGoals.FoundArrivesSilently(true, StoryGoalExecuted.EventType.PDA, true).Should().BeTrue();
        ExperientialGoals.FoundArrivesSilently(true, StoryGoalExecuted.EventType.PDA, false).Should().BeFalse();
        ExperientialGoals.FoundArrivesSilently(false, StoryGoalExecuted.EventType.PDA, true).Should().BeFalse();
        ExperientialGoals.FoundArrivesSilently(true, StoryGoalExecuted.EventType.ENCYCLOPEDIA, true).Should().BeFalse();
    }

    [TestMethod]
    public void OnJoin_UnheardPartnerLinesStayInTheTrackers()
    {
        List<string> completed = ["Goal_BiomeKelpForest", "Goal_LocationAuroraEntry", "Goal_BiomeSafeShallows"];
        HashSet<string> pdaLog = new(StringComparer.OrdinalIgnoreCase) { "Goal_LocationAuroraEntry" };

        // Completed by the partner and left out of this player's PDA log: stays, so this player's visit plays it.
        ExperientialGoals.StaysInTracker("Goal_BiomeKelpForest", true, true, keys, completed, pdaLog).Should().BeTrue();
        // Heard (in the PDA log): removed as before.
        ExperientialGoals.StaysInTracker("Goal_LocationAuroraEntry", true, true, keys, completed, pdaLog).Should().BeFalse();
        // Option off, or not a biome/location PDA line: completed goals are removed as before.
        ExperientialGoals.StaysInTracker("Goal_BiomeKelpForest", true, false, keys, completed, pdaLog).Should().BeFalse();
        ExperientialGoals.StaysInTracker("Goal_BiomeKelpForest", false, true, keys, completed, pdaLog).Should().BeFalse();
        ExperientialGoals.StaysInTracker("Goal_BiomeSafeShallows", true, true, keys, completed, pdaLog).Should().BeFalse();
        // Not completed: stays, as before.
        ExperientialGoals.StaysInTracker("Goal_BiomeGrassyPlateaus", true, false, keys, completed, pdaLog).Should().BeTrue();
    }
}
