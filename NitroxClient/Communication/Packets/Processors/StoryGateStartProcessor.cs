using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>Runs the story moment this game held back, if any (COOP_FEATURES_SPEC G0).</summary>
internal sealed class StoryGateStartProcessor(StoryGates storyGates) : IClientPacketProcessor<StoryGateStart>
{
    private readonly StoryGates storyGates = storyGates;

    public Task Process(ClientProcessorContext context, StoryGateStart packet)
    {
        storyGates.OnStart(packet.Kind);
        return Task.CompletedTask;
    }
}
