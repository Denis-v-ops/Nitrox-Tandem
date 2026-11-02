using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Story;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class StoryHandTarget_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StoryHandTarget t) => t.OnHandClick(default(GUIHand)));

    /// <summary>
    ///     Goals of logs this player found in the world (survivor PDAs are Encyclopedia goals) that wait in the schedule
    ///     (COOP_FEATURES_SPEC D5). <see cref="StoryGoal_Execute_Patch" /> flags them as found when they execute.
    /// </summary>
    /// <remarks>
    ///     <c>OnHandClick</c> calls <c>goal.Trigger()</c>, which only adds the goal to <c>StoryGoalScheduler.schedule</c>;
    ///     the scheduler's <c>Update</c> executes it on a later frame (after the goal's delay).
    /// </remarks>
    public static readonly HashSet<string> FoundKeys = new(StringComparer.OrdinalIgnoreCase);

    public static void Postfix(StoryHandTarget __instance)
    {
        StoryGoal goal = __instance.goal;
        if (goal is { goalType: Story.GoalType.PDA or Story.GoalType.Encyclopedia } && StoryGoalScheduler.main.schedule.Any(scheduled => scheduled.goalKey == goal.key))
        {
            FoundKeys.Add(goal.key);
        }

        if (__instance.destroyGameObject == __instance.gameObject &&
            __instance.gameObject.TryGetNitroxId(out NitroxId destroyId))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(destroyId));
            return;
        }

        if (__instance.informGameObject)
        {
            if (__instance.informGameObject.TryGetComponent(out PrecursorComputerTerminal terminal) &&
                __instance.informGameObject.TryGetNitroxId(out NitroxId terminalId))
            {
                Resolve<Entities>().BroadcastMetadataUpdate(terminalId, new PrecursorComputerTerminalMetadata(terminal.used));
            }
            else if (__instance.informGameObject.TryGetComponent(out GenericConsole console) &&
                     __instance.informGameObject.TryGetNitroxId(out NitroxId consoleId))
            {
                Resolve<Entities>().BroadcastMetadataUpdate(consoleId, new GenericConsoleMetadata(console.gotUsed));
            }
        }
    }
}
