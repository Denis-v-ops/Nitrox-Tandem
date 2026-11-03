using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class VehicleOnPilotModeChangedProcessor(HitchService hitchService) : IAuthPacketProcessor<VehicleOnPilotModeChanged>
{
    private readonly HitchService hitchService = hitchService;

    public async Task Process(AuthProcessorContext context, VehicleOnPilotModeChanged packet)
    {
        context.Sender.PlayerContext.DrivingVehicle = packet.IsPiloting ? packet.VehicleId : null;
        await context.SendToOthersAsync(packet);
        // COOP_FEATURES_SPEC T5: a pilot getting out lets the hitchers go
        await hitchService.PilotChangedAsync(context.Sender, packet.VehicleId, packet.IsPiloting);
    }
}
