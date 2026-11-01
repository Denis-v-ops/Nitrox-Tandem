using System.Reflection;
using NitroxPatcher.Patches.Dynamic;

namespace Nitrox.Test.Patcher.Patches.Dynamic;

[TestClass]
public class StoryGoalCustomEventHandler_NotifyGoalComplete_PatchTest
{
    [TestMethod]
    public void TargetMethodExists()
    {
        MethodInfo targetMethod = (MethodInfo)typeof(StoryGoalCustomEventHandler_NotifyGoalComplete_Patch)
                                              .GetField("TARGET_METHOD", BindingFlags.NonPublic | BindingFlags.Static)!
                                              .GetValue(null);

        Assert.IsNotNull(targetMethod);
        Assert.AreEqual(typeof(StoryGoalCustomEventHandler), targetMethod.DeclaringType);
        Assert.AreEqual(nameof(StoryGoalCustomEventHandler.NotifyGoalComplete), targetMethod.Name);
        Assert.AreEqual(typeof(string), targetMethod.GetParameters().Single().ParameterType);
    }

    [TestMethod]
    public void MatchesOnlyTheGunDeactivateKey()
    {
        Assert.IsTrue(StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.IsGunDeactivateGoal("Goal_Disable_Gun", "Goal_Disable_Gun"));
        Assert.IsTrue(StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.IsGunDeactivateGoal("goal_disable_gun", "Goal_Disable_Gun"));
        Assert.IsFalse(StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.IsGunDeactivateGoal("OnPlayRadioSunbeam4", "Goal_Disable_Gun"));
        Assert.IsFalse(StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.IsGunDeactivateGoal(null, "Goal_Disable_Gun"));
        Assert.IsFalse(StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.IsGunDeactivateGoal(null, null));
        Assert.IsFalse(StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.IsGunDeactivateGoal("", ""));
    }
}
