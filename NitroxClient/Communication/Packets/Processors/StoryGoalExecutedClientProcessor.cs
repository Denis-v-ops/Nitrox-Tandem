using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using Story;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class StoryGoalExecutedClientProcessor(LocalPlayer localPlayer) : IClientPacketProcessor<StoryGoalExecuted>
{
    private readonly LocalPlayer localPlayer = localPlayer;

    public Task Process(ClientProcessorContext context, StoryGoalExecuted packet)
    {
        // COOP_FEATURES_SPEC G3c: the partner's cure finished while this player's own cure cinematic still plays
        if (!CureBallGate.TryDeferRelayedCure(packet.Key, () => Apply(packet)))
        {
            Apply(packet);
        }
        return Task.CompletedTask;
    }

    private void Apply(StoryGoalExecuted packet)
    {
        // D5a: a biome or location line the partner triggered completes here without its PDA entry and voice line.
        // Before the initial sync, CoopSettings holds the defaults (false), so this falls back to today's behaviour.
        bool partnerDiscoveriesSilent = localPlayer.CoopSettings is { PartnerDiscoveriesSilent: true };
        bool silent = ExperientialGoals.CompletesSilently(packet.Key, packet.Type, partnerDiscoveriesSilent, ExperientialGoals.Keys);
        // D5: a log the partner found in the world waits in this game's PDA without its voice line.
        bool foundSilently = !silent && ExperientialGoals.FoundArrivesSilently(packet.Found, packet.Type, partnerDiscoveriesSilent);

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
            else if (foundSilently)
            {
                // StoryGoal.Execute without the sound: OnGoalComplete, then the entry. PDALog.Add with playSound off adds
                // the log entry with its notification. For an Encyclopedia goal (survivor PDAs) Execute calls
                // PDAEncyclopedia.AddAndPlaySound, which queues the voice log and, through Add's verbose, shows a popup
                // that plays it too; instead a quiet Add plus the databank's "new" marker (what verbose adds besides the popup).
                StoryGoalManager.main.OnGoalComplete(packet.Key);
                if (packet.Type == StoryGoalExecuted.EventType.PDA)
                {
                    PDALog.Add(packet.Key, false);
                }
                else if (PDAEncyclopedia.Add(packet.Key, false) != null)
                {
                    NotificationManager.main.Add(NotificationManager.Group.Encyclopedia, packet.Key, 3f);
                }
                Log.Info($"[D5] Log {packet.Key} the partner found added without its voice line");
            }
            else
            {
                StoryGoal.Execute(packet.Key, packet.Type.ToUnity());
            }
        }
    }
}
