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
    public void OtherGoalsSyncAsToday()
    {
        // A story PDA line (not from the biome or location data), and experiential keys of other types.
        ExperientialGoals.CompletesSilently("Story_AuroraWarning1", StoryGoalExecuted.EventType.PDA, true, keys).Should().BeFalse();
        ExperientialGoals.CompletesSilently("Goal_BiomeKelpForest", StoryGoalExecuted.EventType.STORY, true, keys).Should().BeFalse();
        ExperientialGoals.CompletesSilently("Goal_BiomeKelpForest", StoryGoalExecuted.EventType.RADIO, true, keys).Should().BeFalse();
    }
}
