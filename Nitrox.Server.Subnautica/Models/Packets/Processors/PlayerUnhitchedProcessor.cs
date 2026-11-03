using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5): the sender let go of the Seamoth. Relayed to the others only if the
///     server had them holding on.
/// </summary>
internal sealed class PlayerUnhitchedProcessor(HitchService hitchService) : IAuthPacketProcessor<PlayerUnhitched>
{
    private readonly HitchService hitchService = hitchService;

    public Task Process(AuthProcessorContext context, PlayerUnhitched packet) => hitchService.UnhitchedByPlayerAsync(context.Sender, packet.Position);
}
