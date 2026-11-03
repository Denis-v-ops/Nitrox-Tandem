using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EntityMetadataUpdateProcessor(PlayerManager playerManager, EntityRegistry entityRegistry, SimulationOwnershipData simulationOwnershipData, ILogger<EntityMetadataUpdateProcessor> logger)
    : IAuthPacketProcessor<EntityMetadataUpdate>
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly ILogger<EntityMetadataUpdateProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, EntityMetadataUpdate packet)
    {
        if (!entityRegistry.TryGetEntityById(packet.Id, out Entity entity))
        {
            logger.ZLogError($"Entity metadata {packet.NewValue.GetType()} updated on an entity unknown to the server {packet.Id}");
            return;
        }

        if (TryProcessMetadata(context.Sender, entity, packet.NewValue, out EntityMetadata stored, out EntityMetadata relayed))
        {
            entity.Metadata = stored;
            await SendUpdateToVisiblePlayersAsync(context, ReferenceEquals(relayed, packet.NewValue) ? packet : new EntityMetadataUpdate(packet.Id, relayed), entity);
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

    /// <param name="stored">What the entity keeps (saved, sent to players who load it later).</param>
    /// <param name="relayed">What the other players get now.</param>
    private bool TryProcessMetadata(Player sendingPlayer, Entity entity, EntityMetadata metadata, out EntityMetadata stored, out EntityMetadata relayed)
    {
        stored = metadata;
        relayed = metadata;
        switch (metadata)
        {
            case PlayerMetadata playerMetadata:
                return ProcessPlayerMetadata(sendingPlayer, entity, playerMetadata);

            case PowerSourceMetadata:
                return IsPowerProducer(sendingPlayer, entity);

            case ThermalPlantMetadata thermalPlantMetadata:
                return ProcessThermalPlantMetadata(sendingPlayer, entity, thermalPlantMetadata, out stored, out relayed);

            case EscapePodMetadata escapePodMetadata:
                stored = relayed = MergeEscapePodMetadata(sendingPlayer, entity, escapePodMetadata);
                return true;

            // Allow metadata updates from any player by default
            default:
                return true;
        }
    }

    /// <summary>
    ///     Base power (COOP_FEATURES_SPEC B3) comes from the game holding the lock on the source's base, or on the source
    ///     itself when it stands alone (client: <c>BasePowerSync.IsLocalProducer</c>). Every game simulates its own copy, so
    ///     a value from another game would only be that game's guess.
    /// </summary>
    private bool IsPowerProducer(Player sendingPlayer, Entity entity)
    {
        if (simulationOwnershipData.TryGetLock(entity.ParentId ?? entity.Id, out SimulationOwnershipData.PlayerLock playerLock) && playerLock.Player == sendingPlayer)
        {
            return true;
        }
        logger.ZLogDebug($"Ignoring the power of {entity.Id} from {sendingPlayer.Name}, who doesn't hold the lock on {entity.ParentId ?? entity.Id}");
        return false;
    }

    /// <summary>
    ///     The temperature is a ratchet (see <c>ThermalPlant.QueryTemperature</c>'s <c>Mathf.Max</c>), and every client near
    ///     the plant reports the same rise: only a higher one is news. The power (B3, null in temperature-only updates) is
    ///     taken from the lock holder only and kept when an update has none.
    /// </summary>
    private bool ProcessThermalPlantMetadata(Player sendingPlayer, Entity entity, ThermalPlantMetadata metadata, out EntityMetadata stored, out EntityMetadata relayed)
    {
        ThermalPlantMetadata? current = entity.Metadata as ThermalPlantMetadata;
        bool temperatureRose = current == null || metadata.Temperature > current.Temperature;
        float? power = metadata.Power.HasValue && IsPowerProducer(sendingPlayer, entity) ? metadata.Power : null;
        float temperature = current == null ? metadata.Temperature : Math.Max(current.Temperature, metadata.Temperature);

        stored = new ThermalPlantMetadata(temperature, power ?? current?.Power);
        relayed = new ThermalPlantMetadata(temperature, power);
        return temperatureRose || power.HasValue;
    }

    /// <summary>
    ///     Each game reports only whether its own player has opened each hatch (upstream #2859, COOP_FEATURES_SPEC R1b): a
    ///     non-empty list means the sender did. The server adds the sender's name to the players kept so far, so a game can
    ///     neither mark nor clear anyone else, and a rejoin under a new session id keeps the flags.
    /// </summary>
    private static EscapePodMetadata MergeEscapePodMetadata(Player sendingPlayer, Entity entity, EscapePodMetadata incoming)
    {
        EscapePodMetadata? existing = entity.Metadata as EscapePodMetadata;
        return new EscapePodMetadata(incoming.PodRepaired, incoming.RadioRepaired,
                                     MergeHatchUse(existing?.PlayersWithBottomHatchUsed, incoming.PlayersWithBottomHatchUsed.Count > 0, sendingPlayer.Name),
                                     MergeHatchUse(existing?.PlayersWithTopHatchUsed, incoming.PlayersWithTopHatchUsed.Count > 0, sendingPlayer.Name));

        static List<string> MergeHatchUse(List<string>? usedBy, bool usedBySender, string sender)
        {
            List<string> merged = usedBy == null ? [] : [.. usedBy];
            if (usedBySender && !merged.Contains(sender))
            {
                merged.Add(sender);
            }
            return merged;
        }
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
