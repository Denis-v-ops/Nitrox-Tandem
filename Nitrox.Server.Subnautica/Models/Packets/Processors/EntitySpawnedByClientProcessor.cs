using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntitySpawnedByClientProcessor(PlayerManager playerManager, EntityRegistry entityRegistry, WorldEntityManager worldEntityManager, EntitySimulation entitySimulation)
    : IAuthPacketProcessor<EntitySpawnedByClient>
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly EntitySimulation entitySimulation = entitySimulation;

    public async Task Process(AuthProcessorContext context, EntitySpawnedByClient packet)
    {
        Entity entity = packet.Entity;
        entityRegistry.TryGetEntityById(entity.Id, out Entity? previous);

        // If the entity already exists in the registry, it is fine to update.  This is a normal case as the player
        // may have an item in their inventory (that the registry knows about) then wants to spawn it into the world.
        entityRegistry.AddOrUpdate(entity);
        // The previous record may have been tracked in the world (e.g. an item moved from the world into a planter)
        worldEntityManager.StopTrackingEntity(entity.Id);

        SimulatedEntity simulatedEntity = null;
        if (entity is WorldEntity worldEntity)
        {
            worldEntityManager.TrackEntityInTheWorld(worldEntity);
        }

        if (packet.RequireSimulation && entitySimulation.TryAssignEntityToPlayer(entity, context.Sender, true, out simulatedEntity))
        {
            SimulationOwnershipChange ownershipChangePacket = new(simulatedEntity);
            await context.SendToAllAsync(ownershipChangePacket);
        }

        SpawnEntities spawnEntities = new(entity, simulatedEntity, packet.RequireRespawn);
        foreach (Player player in playerManager.GetConnectedPlayers())
        {
            bool isOtherPlayer = player != context.Sender;
            if (isOtherPlayer && player.CanSee(entity))
            {
                await context.SendAsync(spawnEntities, player.SessionId);
            }
            else if (isOtherPlayer && previous != null && player.CanSee(previous))
            {
                // They have the item where it was (in the sender's inventory, a locker, a loaded cell) but not the cell it is
                // in now, so they get no spawn and their copy would stay there: a flare thrown far away stayed unlit in the
                // thrower's hands (COOP_FEATURES_SPEC D6). They drop their copy; the cell's spawn brings it back when they come near.
                await context.SendAsync(new EntityDestroyed(entity.Id), player.SessionId);
            }
        }
    }
}
