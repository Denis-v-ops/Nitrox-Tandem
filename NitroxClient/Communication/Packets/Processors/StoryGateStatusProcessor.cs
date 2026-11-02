using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>Tells the player what a story moment waits for (COOP_FEATURES_SPEC G0).</summary>
internal sealed class StoryGateStatusProcessor(StoryGates storyGates) : IClientPacketProcessor<StoryGateStatus>
{
    private readonly StoryGates storyGates = storyGates;

    public Task Process(ClientProcessorContext context, StoryGateStatus packet)
    {
        storyGates.OnStatus(packet);
        return Task.CompletedTask;
    }
}
