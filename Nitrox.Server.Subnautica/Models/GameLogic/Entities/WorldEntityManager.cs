using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Helper;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <remarks>
///     Regular <see cref="WorldEntity" /> are held in cells and should be registered in <see cref="worldEntitiesByCell" />
///     .
///     But <see cref="GlobalRootEntity" /> are held in their own root object (GlobalRoot) so they should never be
///     registered in cells (they're seeable at all times).
/// </remarks>
// TODO: Refactor to not use internal dictionaries but instead depend on EntityRegistry for data. Maybe add API to EntityRegistry to set custom filter+indexer on entity type (i.e. WorldEntity) for improved world entity lookups?
internal sealed class WorldEntityManager
{
    private readonly BatchEntitySpawner batchEntitySpawner;
    private readonly ConcurrentDictionary<NitroxInt3, Lazy<Task<int>>> batchRegistrationTasks = new();
    private readonly EntityRegistry entityRegistry;

    private readonly Lock globalRootEntitiesLock = new();
    private readonly ILogger<WorldEntityManager> logger;
    private readonly IPacketSender packetSender;
    private readonly PlayerManager playerManager;
    private readonly IProgressReporter progressReporter;

    private readonly Lock worldEntitiesLock = new();

    /// <summary>
    ///     Global root entities that are always visible.
    /// </summary>
    internal Dictionary<NitroxId, GlobalRootEntity> globalRootEntitiesById = [];

    /// <summary>
    ///     World entities can disappear if you go out of range.
    /// </summary>
    internal Dictionary<AbsoluteEntityCell, Dictionary<NitroxId, WorldEntity>> worldEntitiesByCell = [];

    /// <summary>
    ///     Cell in which each entity of <see cref="worldEntitiesByCell" /> is registered, so it can be removed by id even when
    ///     its current record no longer tells where it was (e.g. it became an inventory item).
    /// </summary>
    private Dictionary<NitroxId, AbsoluteEntityCell> cellsByEntityId = [];

    public WorldEntityManager(IPacketSender packetSender, EntityRegistry entityRegistry, BatchEntitySpawner batchEntitySpawner, PlayerManager playerManager, IProgressReporter progressReporter, ILogger<WorldEntityManager> logger)
    {
        this.packetSender = packetSender;
        this.entityRegistry = entityRegistry;
        this.batchEntitySpawner = batchEntitySpawner;
        this.playerManager = playerManager;
        this.logger = logger;
        this.progressReporter = progressReporter;
    }

    public List<GlobalRootEntity> GetGlobalRootEntities(bool rootOnly = false)
    {
        if (rootOnly)
        {
            return GetGlobalRootEntities<GlobalRootEntity>().Where(entity => entity.ParentId == null).ToList();
        }
        return GetGlobalRootEntities<GlobalRootEntity>();
    }

    public List<T> GetGlobalRootEntities<T>() where T : GlobalRootEntity
    {
        lock (globalRootEntitiesLock)
        {
            return new(globalRootEntitiesById.Values.OfType<T>());
        }
    }

    public List<GlobalRootEntity> GetPersistentGlobalRootEntities()
    {
        // TODO: refactor if there are more entities that should not be persisted
        return GetGlobalRootEntities(true).Where(entity =>
        {
            if (entity.Metadata is CyclopsMetadata cyclopsMetadata)
            {
                // Do not save cyclops wrecks
                return !cyclopsMetadata.IsDestroyed;
            }

            return true;
        }).ToList();
    }

    public List<WorldEntity> GetEntities(AbsoluteEntityCell cell)
    {
        lock (worldEntitiesLock)
        {
            if (!worldEntitiesByCell.TryGetValue(cell, out Dictionary<NitroxId, WorldEntity> batchEntities))
            {
                return [];
            }

            List<WorldEntity> entities = new(batchEntities.Count);
            List<NitroxId> staleIds = null;
            foreach (WorldEntity entity in batchEntities.Values)
            {
                // An entry whose entity was removed or isn't a cell entity anymore would make players respawn it in this cell
                if (!entityRegistry.TryGetEntityById(entity.Id, out Entity current) || current is not WorldEntity || current is GlobalRootEntity)
                {
                    logger.ZLogWarning($"Removing stale entry of {entity.Id} ({entity.TechType}) from cell {cell}, it is now registered as {current?.GetType().Name ?? "nothing"}");
                    (staleIds ??= []).Add(entity.Id);
                    continue;
                }
                entities.Add(entity);
            }

            if (staleIds != null)
            {
                foreach (NitroxId staleId in staleIds)
                {
                    UnregisterWorldEntityFromCell(staleId, cell);
                }
            }

            return entities;
        }
    }

    public bool TryUpdateEntityPosition(NitroxId id, NitroxVector3 position, NitroxQuaternion rotation, out AbsoluteEntityCell? newCell, out WorldEntity worldEntity)
    {
        lock (worldEntitiesLock)
        {
            if (!entityRegistry.TryGetEntityById(id, out worldEntity))
            {
                logger.ZLogWarningOnce($"can't update entity position of {id} because it isn't registered");
                newCell = null;
                return false;
            }

            // Return early because a GlobalRootEntity doesn't have an AbsoluteEntityCell, thus it would throw an exception
            if (worldEntity is GlobalRootEntity)
            {
                worldEntity.Transform.Position = position;
                worldEntity.Transform.Rotation = rotation;
                newCell = null;
                return true;
            }

            AbsoluteEntityCell oldCell = worldEntity.AbsoluteEntityCell;

            worldEntity.Transform.Position = position;
            worldEntity.Transform.Rotation = rotation;

            newCell = worldEntity.AbsoluteEntityCell;

            if (oldCell != newCell)
            {
                EntitySwitchedCells(worldEntity, oldCell, newCell);
            }

            return true;
        }
    }

    public Optional<Entity> RemoveGlobalRootEntity(NitroxId entityId, bool removeFromRegistry = true)
    {
        Optional<Entity> removedEntity = Optional.Empty;
        lock (globalRootEntitiesLock)
        {
            if (removeFromRegistry)
            {
                // In case there were player entities under the removed entity, we need to reparent them to the GlobalRoot
                // to make sure that they won't be removed
                if (entityRegistry.TryGetEntityById(entityId, out GlobalRootEntity globalRootEntity))
                {
                    MovePlayerChildrenToRoot(globalRootEntity);
                }
                removedEntity = entityRegistry.RemoveEntity(entityId);
            }
            globalRootEntitiesById.Remove(entityId);
        }
        return removedEntity;
    }

    public void MovePlayerChildrenToRoot(GlobalRootEntity globalRootEntity)
    {
        List<PlayerEntity> playerEntities = FindPlayerEntitiesInChildren(globalRootEntity);
        foreach (PlayerEntity childPlayerEntity in playerEntities)
        {
            // Reparent the entity on top of GlobalRoot
            globalRootEntity.ChildEntities.Remove(childPlayerEntity);
            childPlayerEntity.ParentId = null;

            // Make sure the PlayerEntity is correctly registered
            AddOrUpdateGlobalRootEntity(childPlayerEntity);
        }
    }

    public void TrackEntityInTheWorld(WorldEntity entity)
    {
        if (entity is GlobalRootEntity globalRootEntity)
        {
            AddOrUpdateGlobalRootEntity(globalRootEntity, false);
            return;
        }

        RegisterWorldEntity(entity);
    }

    /// <summary>
    ///     Automatically registers a WorldEntity in its AbsoluteEntityCell
    /// </summary>
    /// <remarks>
    ///     The provided should NOT be a GlobalRootEntity (they don't stand in cells)
    /// </remarks>
    public void RegisterWorldEntity(WorldEntity entity)
    {
        RegisterWorldEntityInCell(entity, entity.AbsoluteEntityCell);
    }

    public void RegisterWorldEntityInCell(WorldEntity entity, AbsoluteEntityCell cell)
    {
        lock (worldEntitiesLock)
        {
            // An entity is only in one cell, drop the entry it may have left elsewhere
            if (cellsByEntityId.TryGetValue(entity.Id, out AbsoluteEntityCell previousCell) && previousCell != cell)
            {
                UnregisterWorldEntityFromCell(entity.Id, previousCell);
            }
            if (!worldEntitiesByCell.TryGetValue(cell, out Dictionary<NitroxId, WorldEntity> worldEntitiesInCell))
            {
                worldEntitiesInCell = worldEntitiesByCell[cell] = [];
            }
            worldEntitiesInCell[entity.Id] = entity;
            cellsByEntityId[entity.Id] = cell;
        }
    }

    /// <summary>
    ///     Replaces all cell registrations, used when loading a save. Only entities that would be in a cell during a
    ///     session are registered (see <see cref="BelongsInCell" />).
    /// </summary>
    public void SetWorldEntities(IEnumerable<WorldEntity> entities)
    {
        lock (worldEntitiesLock)
        {
            worldEntitiesByCell = [];
            cellsByEntityId = [];
            foreach (WorldEntity entity in entities)
            {
                if (BelongsInCell(entity))
                {
                    RegisterWorldEntity(entity);
                }
            }
        }
    }

    /// <summary>
    ///     Whether a world entity record stands in a cell: a top-level entity, or one inside a hierarchy of cell entities
    ///     (what the batch spawner registers, e.g. the placeholders of a wreck). Not one whose parent, or any ancestor, is
    ///     a container, an inventory or a global root (base, vehicle): those are sent along with their parent, and
    ///     <c>EntityReparentedProcessor</c> drops their cell entry when they are moved there (S7 #2691, plan item 5).
    /// </summary>
    public bool BelongsInCell(WorldEntity entity)
    {
        if (entity is GlobalRootEntity)
        {
            return false;
        }
        Entity current = entity;
        // Bounded in case of a broken (cyclic) parent chain
        for (int depth = 0; current.ParentId != null && depth < 64; depth++)
        {
            if (!entityRegistry.TryGetEntityById(current.ParentId, out Entity parent))
            {
                // Orphaned record: it is placed by its own transform, as before
                return true;
            }
            if (parent is not WorldEntity || parent is GlobalRootEntity)
            {
                return false;
            }
            current = parent;
        }
        return true;
    }

    /// <summary>
    ///     Unregisters a WorldEntity from the cell it was registered in
    /// </summary>
    public void UnregisterWorldEntity(WorldEntity entity)
    {
        UnregisterWorldEntity(entity.Id);
    }

    /// <inheritdoc cref="UnregisterWorldEntity(WorldEntity)" />
    public void UnregisterWorldEntity(NitroxId entityId)
    {
        lock (worldEntitiesLock)
        {
            if (cellsByEntityId.TryGetValue(entityId, out AbsoluteEntityCell cell))
            {
                UnregisterWorldEntityFromCell(entityId, cell);
            }
        }
    }

    public void UnregisterWorldEntityFromCell(NitroxId entityId, AbsoluteEntityCell cell)
    {
        lock (worldEntitiesLock)
        {
            if (worldEntitiesByCell.TryGetValue(cell, out Dictionary<NitroxId, WorldEntity> worldEntitiesInCell) && worldEntitiesInCell.Remove(entityId) &&
                cellsByEntityId.TryGetValue(entityId, out AbsoluteEntityCell registeredCell) && registeredCell == cell)
            {
                cellsByEntityId.Remove(entityId);
            }
        }
    }

    public async Task LoadAllUnspawnedEntitiesAsync(CancellationToken token)
    {
        int totalBatches = SubnauticaMap.DimensionsInBatches.X * SubnauticaMap.DimensionsInBatches.Y * SubnauticaMap.DimensionsInBatches.Z;
        int batchesLoaded = 0;

        for (int x = 0; x < SubnauticaMap.DimensionsInBatches.X; x++)
        {
            token.ThrowIfCancellationRequested();
            for (int y = 0; y < SubnauticaMap.DimensionsInBatches.Y; y++)
            {
                for (int z = 0; z < SubnauticaMap.DimensionsInBatches.Z; z++)
                {
                    int spawned = await LoadUnspawnedEntitiesAsync(new(x, y, z), true);

                    logger.LogEntitiesLoadedFromBatch(spawned, x, y, z);

                    batchesLoaded++;
                }
            }

            if (batchesLoaded > 0)
            {
                float progress = (float)batchesLoaded / totalBatches;
                int percentage = (int)(100f * progress);
                logger.ZLogInformation($"Loading : {percentage}%");
                await progressReporter.ReportProgressAsync($"Loading entities: {percentage}%", progress);
            }
        }
    }

    public Task<int> LoadUnspawnedEntitiesAsync(NitroxInt3 batchId, bool suppressLogs)
    {
        return batchRegistrationTasks.GetOrAdd(batchId, id => new Lazy<Task<int>>(() => LoadAndRegisterBatchInternalAsync(id, suppressLogs))).Value;
    }

    private async Task<int> LoadAndRegisterBatchInternalAsync(NitroxInt3 batchId, bool suppressLogs)
    {
        List<Entity> spawnedEntities = await batchEntitySpawner.LoadUnspawnedEntitiesAsync(batchId, suppressLogs);

        List<WorldEntity> entitiesInCells = spawnedEntities.Where(entity => typeof(WorldEntity).IsAssignableFrom(entity.GetType()) &&
                                                                            entity.GetType() != typeof(CellRootEntity) &&
                                                                            entity.GetType() != typeof(GlobalRootEntity))
                                                           .Cast<WorldEntity>()
                                                           .ToList();

        // UWE stores entities serialized with a handful of parent cell roots.  These only represent a small fraction of all possible cell
        // roots that could exist.  There is no reason for the server to know about these and much easier to consider top-level world entities
        // as positioned globally and not locally.  Thus, we promote cell root children to top level and throw the cell roots away. 
        foreach (CellRootEntity cellRoot in spawnedEntities.OfType<CellRootEntity>())
        {
            foreach (WorldEntity worldEntity in cellRoot.ChildEntities.Cast<WorldEntity>())
            {
                worldEntity.ParentId = null;
                worldEntity.Transform.SetParent(null);
                entitiesInCells.Add(worldEntity);
            }

            cellRoot.ChildEntities = new List<Entity>();
        }
        // Specific type of entities which is not parented to a CellRootEntity
        entitiesInCells.AddRange(spawnedEntities.OfType<SerializedWorldEntity>());

        entityRegistry.AddEntitiesIgnoringDuplicate(entitiesInCells.OfType<Entity>().ToList());

        foreach (WorldEntity entity in entitiesInCells)
        {
            RegisterWorldEntity(entity);
        }

        return entitiesInCells.Count;
    }

    public void StopTrackingEntity(WorldEntity entity)
    {
        if (entity is GlobalRootEntity)
        {
            RemoveGlobalRootEntity(entity.Id, false);
        }
        else
        {
            UnregisterWorldEntity(entity);
        }
    }

    /// <summary>
    ///     Stops tracking the entity with this id in the open world, whatever its current record is.
    /// </summary>
    public void StopTrackingEntity(NitroxId entityId)
    {
        lock (globalRootEntitiesLock)
        {
            globalRootEntitiesById.Remove(entityId);
        }
        UnregisterWorldEntity(entityId);
    }

    public bool TryDestroyEntity(NitroxId entityId, [NotNullWhen(true)] out Entity? entity)
    {
        Optional<Entity> optEntity = entityRegistry.RemoveEntity(entityId);

        if (!optEntity.HasValue)
        {
            entity = null;
            return false;
        }
        entity = optEntity.Value;

        StopTrackingEntity(entityId);

        return true;
    }

    /// <summary>
    ///     To avoid risking not having the same entity in <see cref="globalRootEntitiesById" /> and in EntityRegistry, we
    ///     update both at the same time.
    /// </summary>
    public void AddOrUpdateGlobalRootEntity(GlobalRootEntity entity, bool addOrUpdateRegistry = true)
    {
        lock (globalRootEntitiesLock)
        {
            if (addOrUpdateRegistry)
            {
                entityRegistry.AddOrUpdate(entity);
            }
            globalRootEntitiesById[entity.Id] = entity;
        }
    }

    private void EntitySwitchedCells(WorldEntity entity, AbsoluteEntityCell oldCell, AbsoluteEntityCell newCell)
    {
        if (entity is GlobalRootEntity)
        {
            return; // We don't care what cell a global root entity resides in.  Only phasing entities.
        }

        if (oldCell != newCell)
        {
            lock (worldEntitiesLock)
            {
                // Specifically remove entity from oldCell
                UnregisterWorldEntityFromCell(entity.Id, oldCell);

                // Automatically add entity to its new cell
                RegisterWorldEntityInCell(entity, newCell);

                // It can happen for some players that the entity moves to a loaded cell of theirs, but that they hadn't spawned it in the first place
                foreach (Player player in playerManager.ConnectedPlayers())
                {
                    if (player.HasCellLoaded(newCell) && !player.HasCellLoaded(oldCell))
                    {
                        packetSender.SendPacketAsync(new SpawnEntities(entity), player.SessionId);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Iterative breadth-first search which gets all children player entities in <paramref name="parentEntity" />'s
    ///     hierarchy.
    /// </summary>
    private List<PlayerEntity> FindPlayerEntitiesInChildren(Entity parentEntity)
    {
        List<PlayerEntity> playerEntities = [];
        List<Entity> entitiesToSearch = [parentEntity];

        while (entitiesToSearch.Count > 0)
        {
            Entity currentEntity = entitiesToSearch[^1];
            entitiesToSearch.RemoveAt(entitiesToSearch.Count - 1);

            if (currentEntity is PlayerEntity playerEntity)
            {
                playerEntities.Add(playerEntity);
            }
            else
            {
                entitiesToSearch.InsertRange(0, currentEntity.ChildEntities);
            }
        }
        return playerEntities;
    }
}
