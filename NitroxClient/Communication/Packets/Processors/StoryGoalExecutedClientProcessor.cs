using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using Story;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class StoryGoalExecutedClientProcessor(LocalPlayer localPlayer) : IClientPacketProcessor<StoryGoalExecuted>
{
    private readonly LocalPlayer localPlayer = localPlayer;

    public Task Process(ClientProcessorContext context, StoryGoalExecuted packet)
    {
        // D5a: a biome or location line the partner triggered completes here without its PDA entry and voice line.
        // Before the initial sync, CoopSettings holds the defaults (false), so this falls back to today's behaviour.
        bool silent = ExperientialGoals.CompletesSilently(packet.Key, packet.Type, localPlayer.CoopSettings is { PartnerDiscoveriesSilent: true }, ExperientialGoals.Keys);

        // A silent goal stays in this game's schedule: if this player was there too, their own tracker already
        // scheduled it (and removed it from its list), so the scheduled Execute is their only chance to hear it.
        if (!silent)
        {
            StoryGoalScheduler.main.schedule.RemoveAllFast(packet.Key, static (goal, packetGoalKey) => goal.goalKey == packetGoalKey);
        }

        using (PacketSuppressor<StoryGoalExecuted>.Suppress())
        using (PacketSuppressor<PDALogEntryAdd>.Suppress())
        using (PacketSuppressor<KnownTechEntryAdd>.Suppress()) // StoryGoalManager => OnGoalUnlockTracker => UnlockBlueprintData => KnownTech.Add
        using (PacketSuppressor<PDAEncyclopediaEntryAdd>.Suppress())
        {
            if (silent)
            {
                // StoryGoal.Execute for a PDA goal without PDALog.Add: the unlocks, signals and listeners still run.
                StoryGoalManager.main.OnGoalComplete(packet.Key);
                Log.Info($"[D5a] Partner discovery {packet.Key} completed silently");
            }
            else
            {
                StoryGoal.Execute(packet.Key, packet.Type.ToUnity());
            }
        }
        return Task.CompletedTask;
    }
}
