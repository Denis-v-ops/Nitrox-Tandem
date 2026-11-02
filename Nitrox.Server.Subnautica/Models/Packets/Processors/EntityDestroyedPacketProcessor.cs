using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntityDestroyedPacketProcessor(EntitySimulation entitySimulation, WorldEntityManager worldEntityManager) : IAuthPacketProcessor<EntityDestroyed>
{
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;

    public async Task Process(AuthProcessorContext context, EntityDestroyed packet)
    {
        entitySimulation.EntityDestroyed(packet.Id);

        if (worldEntityManager.TryDestroyEntity(packet.Id, out Entity? entity))
        {
            if (entity is VehicleEntity vehicleEntity)
            {
                worldEntityManager.MovePlayerChildrenToRoot(vehicleEntity);
            }

            // Sent to every other player, not only to those who can see the entity: a scanner room keeps the resources of cells
            // that aren't loaded anymore, and must forget the destroyed ones (#2046, #2151). The server can't tell which
            // entities are tracked resources (that's a component on the game's prefab).
            await context.SendToOthersAsync(packet);
        }
    }
}
