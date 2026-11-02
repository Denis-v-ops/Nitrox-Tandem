using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntityMetadataUpdateProcessor(PlayerManager playerManager, EntityRegistry entityRegistry, ILogger<EntityMetadataUpdateProcessor> logger) : IAuthPacketProcessor<EntityMetadataUpdate>
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<EntityMetadataUpdateProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, EntityMetadataUpdate packet)
    {
        if (!entityRegistry.TryGetEntityById(packet.Id, out Entity entity))
        {
            logger.ZLogError($"Entity metadata {packet.NewValue.GetType()} updated on an entity unknown to the server {packet.Id}");
            return;
        }

        if (TryProcessMetadata(context.Sender, entity, packet.NewValue))
        {
            // A rocket report is one player's whole view of the rocket: two players acting within one round trip each lack
            // the other's action, so it's merged into what the server has (MULTINAUGHTICA_SPEC M2). The games only ever add
            // stages and checks, so relaying the report as it is lets both converge.
            entity.Metadata = packet.NewValue is RocketMetadata rocketMetadata ? RocketMetadata.Merge(entity.Metadata as RocketMetadata, rocketMetadata) : packet.NewValue;
            await SendUpdateToVisiblePlayersAsync(context, packet, entity);
        }
    }

    private async Task SendUpdateToVisiblePlayersAsync(AuthProcessorContext context, EntityMetadataUpdate packet, Entity entity)
    {
        foreach (Player player in playerManager.GetConnectedPlayers())
        {
            bool updateVisibleToPlayer = player.CanSee(entity);
            if (player != context.Sender && updateVisibleToPlayer)
            {
                await context.SendAsync(packet, player.SessionId);
            }
        }
    }

    private bool TryProcessMetadata(Player sendingPlayer, Entity entity, EntityMetadata metadata)
    {
        return metadata switch
        {
            PlayerMetadata playerMetadata => ProcessPlayerMetadata(sendingPlayer, entity, playerMetadata),

            // temperature is a ratchet (see ThermalPlant.QueryTemperature's Mathf.Max), so drop stale updates instead of
            // relaying them: every client near a thermal plant reports the same rise, and only the first one is news
            ThermalPlantMetadata thermalPlantMetadata => entity.Metadata is not ThermalPlantMetadata currentMetadata || thermalPlantMetadata.Temperature > currentMetadata.Temperature,

            // a radiation leak only gains health: a stale update (two players welding one leak) must not make it leak again
            // and reset its fix time (MULTINAUGHTICA_SPEC M2)
            RadiationMetadata radiationMetadata => entity.Metadata is not RadiationMetadata currentRadiation || RadiationMetadata.IsProgress(currentRadiation.Health, radiationMetadata.Health),

            // Allow metadata updates from any player by default
            _ => true
        };
    }

    private bool ProcessPlayerMetadata(Player sendingPlayer, Entity entity, PlayerMetadata metadata)
    {
        if (sendingPlayer.GameObjectId == entity.Id)
        {
            sendingPlayer.EquippedItems.Clear();
            foreach (PlayerMetadata.EquippedItem item in metadata.EquippedItems)
            {
                sendingPlayer.EquippedItems.Add(item.Slot, item.Id);
            }

            return true;
        }

        logger.ZLogWarningOnce($"Player {sendingPlayer.Name} tried updating metadata of another player's entity {entity.Id}");
        return false;
    }
}
