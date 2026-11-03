using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): the sender went down. <see cref="RescueService" /> decides the window.
/// </summary>
internal sealed class PlayerDownedProcessor(RescueService rescueService) : IAuthPacketProcessor<PlayerDowned>
{
    private readonly RescueService rescueService = rescueService;

    public Task Process(AuthProcessorContext context, PlayerDowned packet) => rescueService.PlayerWentDownAsync(context.Sender);
}
