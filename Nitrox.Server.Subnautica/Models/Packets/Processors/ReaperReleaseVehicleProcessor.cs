using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Packets.Processors.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     M4: a Reaper's release goes to the players who can see the Reaper. Temporary, nothing is stored.
/// </summary>
internal sealed class ReaperReleaseVehicleProcessor(PlayerManager playerManager, EntityRegistry entityRegistry) : TransmitIfCanSeePacketProcessor<ReaperReleaseVehicle>(playerManager, entityRegistry)
{
    public override async Task Process(AuthProcessorContext context, ReaperReleaseVehicle packet) => await TransmitIfCanSeeEntitiesAsync(context, packet, [packet.ReaperId]);
}
