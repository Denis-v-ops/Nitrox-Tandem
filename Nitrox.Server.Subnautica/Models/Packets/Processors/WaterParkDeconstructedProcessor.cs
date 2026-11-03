using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Bases;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class WaterParkDeconstructedProcessor(BuildingManager buildingManager, EntitySimulation entitySimulation) : BuildingProcessor<WaterParkDeconstructed>(buildingManager, entitySimulation)
{
    public override async Task Process(AuthProcessorContext context, WaterParkDeconstructed packet)
    {
        if (BuildingManager.ReplacePieceByGhost(context.Sender, packet, out Entity removedEntity, out int operationId) &&
            BuildingManager.CreateWaterParkPiece(packet, removedEntity))
        {
            // The split-off tank is a new global root. Its eggs hatch and its fish breed and grow only on the game holding
            // its lock (WaterParkCreature_BornAsync_Patch.CanBeBorn), so it needs a holder now, not after the next restart (#2538)
            await TryClaimBuildPieceAsync(context, packet.NewWaterPark);
            packet.BaseData = null;
            await SendToOtherPlayersWithOperationIdAsync(context, packet, operationId);
        }
    }
}
