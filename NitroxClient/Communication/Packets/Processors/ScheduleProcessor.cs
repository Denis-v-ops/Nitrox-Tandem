using System.Linq;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using Story;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class ScheduleProcessor(LocalPlayer localPlayer) : IClientPacketProcessor<Schedule>
{
    private readonly LocalPlayer localPlayer = localPlayer;

    public Task Process(ClientProcessorContext context, Schedule schedulePacket)
    {
        ScheduledGoal goal = new()
        {
            goalKey = schedulePacket.Key,
            goalType = (Story.GoalType)schedulePacket.Type,
            timeExecute = schedulePacket.TimeExecute
        };
        if (ShouldSchedule(goal))
        {
            StoryGoalScheduler.main.schedule.Add(goal);
        }
        Log.Debug($"Processed a Schedule packet [Key: {goal.goalKey}, Type: {goal.goalType}, TimeExecute: {goal.timeExecute}]");
        return Task.CompletedTask;
    }

    private bool ShouldSchedule(ScheduledGoal goal)
    {
        return goal.timeExecute >= DayNightCycle.main.timePassedAsDouble && !IsAlreadyKnown(goal.goalKey) && !IsPartnersOwnFirst(goal);
    }

    private bool IsAlreadyKnown(string goalKey)
    {
        return StoryGoalScheduler.main.schedule.Any(g => g.goalKey == goalKey) || //  Scheduled
               StoryGoalManager.main.completedGoals.Contains(goalKey); // Completed
    }

    /// <summary>
    ///     D5a: a delayed biome or location line the partner triggered isn't executed here; the partner's
    ///     StoryGoalExecuted completes it silently when it's due.
    /// </summary>
    private bool IsPartnersOwnFirst(ScheduledGoal goal)
    {
        return ExperientialGoals.IsOwnFirst(goal.goalKey, goal.goalType == Story.GoalType.PDA, localPlayer.CoopSettings is { PartnerDiscoveriesSilent: true }, ExperientialGoals.Keys);
    }
}
