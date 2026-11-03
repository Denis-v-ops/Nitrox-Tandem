using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): the sender finished a revive hold on a downed partner.
/// </summary>
internal sealed class PlayerReviveRequestProcessor(RescueService rescueService) : IAuthPacketProcessor<PlayerReviveRequest>
{
    private readonly RescueService rescueService = rescueService;

    public Task Process(AuthProcessorContext context, PlayerReviveRequest packet) => rescueService.ReviveAsync(context.Sender, packet);
}
