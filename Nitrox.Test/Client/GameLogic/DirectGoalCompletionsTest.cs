namespace NitroxClient.GameLogic;

/// <summary>MULTINAUGHTICA_SPEC M2: which goals completed outside StoryGoal.Execute are sent to the server.</summary>
[TestClass]
public class DirectGoalCompletionsTest
{
    [TestMethod]
    public void ADoorKeyCompletedDirectly_IsReported()
    {
        // PrecursorGlobalKeyActivator.ToggleDoor -> StoryGoalManager.OnGoalComplete(doorActivationKey)
        DirectGoalCompletions.ShouldReport("PrecursorKey_Purple_Door", newlyCompleted: true, insideExecute: false, initialSyncCompleted: true).Should().BeTrue();
    }

    [TestMethod]
    public void GoalsOfStoryGoalExecute_AreReportedThereAlready()
    {
        DirectGoalCompletions.ShouldReport("Goal_Disable_Gun", newlyCompleted: true, insideExecute: true, initialSyncCompleted: true).Should().BeFalse();
    }

    [TestMethod]
    public void GoalsCompleteAlready_OrBeforeTheSync_AreNotReported()
    {
        DirectGoalCompletions.ShouldReport("PrecursorKey_Purple_Door", newlyCompleted: false, insideExecute: false, initialSyncCompleted: true).Should().BeFalse();
        DirectGoalCompletions.ShouldReport("PrecursorKey_Purple_Door", newlyCompleted: true, insideExecute: false, initialSyncCompleted: false).Should().BeFalse();
    }

    [TestMethod]
    [DataRow("Trigger_PDAIntroBegin")]
    [DataRow("Trigger_PDAIntroEnd")]
    [DataRow("trigger_pdaintroend")]
    public void EachPlayersIntroTriggers_StayTheirOwn(string key)
    {
        DirectGoalCompletions.ShouldReport(key, newlyCompleted: true, insideExecute: false, initialSyncCompleted: true).Should().BeFalse();
    }
}
