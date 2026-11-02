using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Packets.Processors.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     M4: a Reaper's grab goes to the players who can see the Reaper and the vehicle. Temporary, nothing is stored.
/// </summary>
internal sealed class ReaperGrabVehicleProcessor(PlayerManager playerManager, EntityRegistry entityRegistry) : TransmitIfCanSeePacketProcessor<ReaperGrabVehicle>(playerManager, entityRegistry)
{
    public override async Task Process(AuthProcessorContext context, ReaperGrabVehicle packet) => await TransmitIfCanSeeEntitiesAsync(context, packet, [packet.ReaperId, packet.VehicleId]);
}
