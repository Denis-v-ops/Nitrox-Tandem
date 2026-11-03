using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.InitialSync;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class AuroraAndTimeUpdateProcessor(TimeManager timeManager, InitialSyncDeferral initialSyncDeferral) : IClientPacketProcessor<AuroraAndTimeUpdate>
{
    private readonly TimeManager timeManager = timeManager;

    public Task Process(ClientProcessorContext context, AuroraAndTimeUpdate packet)
    {
        // MULTINAUGHTICA_SPEC M2: before the initial sync the Aurora may not exist yet (CrashedShipExploder.main), and the
        // sync's older snapshot would overwrite this update: it's applied after the sync
        if (initialSyncDeferral.Defer(() => Process(context, packet)))
        {
            return Task.CompletedTask;
        }

        timeManager.ProcessUpdate(packet.TimeData.TimePacket);
        StoryManager.UpdateAuroraData(packet.TimeData.AuroraEventData);
        timeManager.AuroraRealExplosionTime = packet.TimeData.AuroraEventData.AuroraRealExplosionTime;
        if (packet.Restore)
        {
            StoryManager.RestoreAurora();
        }
        return Task.CompletedTask;
    }
}
