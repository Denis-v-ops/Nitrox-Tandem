using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Packets.Processors.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     M4: a Crabsquid's EMP blast goes to the players who can see the creature. Temporary, nothing is stored.
/// </summary>
internal sealed class EmpFiredProcessor(PlayerManager playerManager, EntityRegistry entityRegistry) : TransmitIfCanSeePacketProcessor<EmpFired>(playerManager, entityRegistry)
{
    public override async Task Process(AuthProcessorContext context, EmpFired packet) => await TransmitIfCanSeeEntitiesAsync(context, packet, [packet.CreatureId]);
}
