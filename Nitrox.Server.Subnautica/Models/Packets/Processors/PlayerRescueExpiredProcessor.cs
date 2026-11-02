using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): the sender died while down (drowned). Without this processor the default
///     one would relay it, and the server would still count them as down.
/// </summary>
internal sealed class PlayerRescueExpiredProcessor(RescueService rescueService) : IAuthPacketProcessor<PlayerRescueExpired>
{
    private readonly RescueService rescueService = rescueService;

    public Task Process(AuthProcessorContext context, PlayerRescueExpired packet) => rescueService.PlayerDiedAsync(context.Sender);
}
