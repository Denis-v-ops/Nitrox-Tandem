using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntityReparentedProcessor(EntityRegistry entityRegistry, WorldEntityManager worldEntityManager, ILogger<EntityReparentedProcessor> logger) : IAuthPacketProcessor<EntityReparented>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly ILogger<EntityReparentedProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, EntityReparented packet)
    {
        if (!entityRegistry.TryGetEntityById(packet.Id, out Entity entity))
        {
            logger.ZLogError($"Couldn't find entity for {packet.Id}");
            return;
        }
        if (!entityRegistry.TryGetEntityById(packet.NewParentId, out Entity parentEntity))
        {
            logger.ZLogError($"Couldn't find parent entity for {packet.NewParentId}");
            return;
        }

        entityRegistry.ReparentEntity(packet.Id, packet.NewParentId);
        // The entity is now sent along with its parent, a cell entry would make players respawn it where it was
        if (entity is not GlobalRootEntity)
        {
            worldEntityManager.UnregisterWorldEntity(packet.Id);
        }
        await context.SendToOthersAsync(packet);
    }
}
