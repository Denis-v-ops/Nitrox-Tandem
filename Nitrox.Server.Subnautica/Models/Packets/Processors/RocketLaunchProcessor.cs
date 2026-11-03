using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     A game launched its rocket (COOP_FEATURES_SPEC G2). Each game launches its own player once the
///     <see cref="StoryGateKind.ROCKET_LAUNCH" /> gate starts; the launch is relayed only after that, so a launch the gate
///     didn't allow can't take a partner along or leave them behind. Before G2 the default processor relayed it at once.
/// </summary>
internal sealed class RocketLaunchProcessor(StoryGateService storyGates, ILogger<RocketLaunchProcessor> logger) : IAuthPacketProcessor<RocketLaunch>
{
    private readonly StoryGateService storyGates = storyGates;
    private readonly ILogger<RocketLaunchProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, RocketLaunch packet)
    {
        if (!storyGates.HasStarted(StoryGateKind.ROCKET_LAUNCH))
        {
            logger.ZLogWarning($"{context.Sender.Name} launched rocket {packet.RocketId} before the launch gate started; not relayed");
            return;
        }
        await context.SendToOthersAsync(packet);
    }
}
