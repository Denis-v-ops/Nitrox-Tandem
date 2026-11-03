using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5): the sender asks to hold on to a Seamoth. <see cref="HitchService" />
///     checks it; a refused request is only logged (the hitcher's game waits for the answer before it grabs on).
/// </summary>
internal sealed class PlayerHitchedProcessor(HitchService hitchService) : IAuthPacketProcessor<PlayerHitched>
{
    private readonly HitchService hitchService = hitchService;

    public Task Process(AuthProcessorContext context, PlayerHitched packet) => hitchService.HitchAsync(context.Sender, packet.VehicleId);
}
