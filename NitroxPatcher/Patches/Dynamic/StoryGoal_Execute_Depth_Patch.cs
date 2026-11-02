using System.Reflection;
using NitroxClient.GameLogic;
using Story;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Counts the running <see cref="StoryGoal.Execute" /> calls, so that <see cref="StoryGoalManager_OnGoalComplete_Patch" />
///     doesn't report a goal that <see cref="StoryGoal_Execute_Patch" /> reports (MULTINAUGHTICA_SPEC M2). A separate patch so
///     that one stays as it is.
/// </summary>
public sealed partial class StoryGoal_Execute_Depth_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => StoryGoal.Execute(default, default));

    public static void Prefix()
    {
        DirectGoalCompletions.ExecuteDepth++;
    }

    public static void Finalizer()
    {
        DirectGoalCompletions.ExecuteDepth--;
    }
}
