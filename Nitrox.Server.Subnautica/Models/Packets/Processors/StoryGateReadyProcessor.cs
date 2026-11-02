using Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     A game held a story moment back and asks for it (COOP_FEATURES_SPEC G0). Without this processor the default one
///     would relay the ask to the others and nothing would ever start.
/// </summary>
internal sealed class StoryGateReadyProcessor(StoryGateService storyGates) : IAuthPacketProcessor<StoryGateReady>
{
    private readonly StoryGateService storyGates = storyGates;

    public async Task Process(AuthProcessorContext context, StoryGateReady packet)
    {
        await storyGates.SetReadyAsync(context.Sender, packet.Kind, packet.Ready);
    }
}
