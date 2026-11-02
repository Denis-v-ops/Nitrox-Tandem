using System.Reflection;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using Story;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Sends the goals the game completes directly, outside <see cref="StoryGoal.Execute" /> (<see cref="DirectGoalCompletions" />),
///     as Story goals, so the server keeps them and every game, also after a restart, has them (MULTINAUGHTICA_SPEC M2). A
///     remote goal is completed under the suppressor of its packet, so it isn't sent back.
/// </summary>
public sealed partial class StoryGoalManager_OnGoalComplete_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StoryGoalManager t) => t.OnGoalComplete(default));

    /// <param name="__result">True if the goal wasn't complete before.</param>
    public static void Postfix(string key, bool __result)
    {
        if (!__result)
        {
            return;
        }

        // Local or remote: a teleporter pair goal switches the pair on in this game
        TeleporterPairs.OnGoalCompleted(key);

        if (DirectGoalCompletions.ShouldReport(key, __result, DirectGoalCompletions.ExecuteDepth > 0, Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted))
        {
            Resolve<IPacketSender>().Send(new StoryGoalExecuted(key, Story.GoalType.Story.ToDto()));
        }
    }
}
