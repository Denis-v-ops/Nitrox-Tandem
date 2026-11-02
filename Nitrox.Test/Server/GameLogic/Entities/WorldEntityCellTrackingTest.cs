using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Packets.Processors;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.GameLogic.Entities;

/// <summary>
///     Items that leave the open world must not keep an entry in their old cell, otherwise players respawn them there (#2691).
/// </summary>
[TestClass]
public class WorldEntityCellTrackingTest
{
    private static readonly NitroxVector3 positionA = new(10, -20, 10);
    private static readonly NitroxVector3 positionB = new(600, -20, 600);

    private IPacketSender packetSender;
    private EntityRegistry entityRegistry;
    private WorldEntityManager worldEntityManager;
    private AuthProcessorContext context;

    [TestInitialize]
    public void Setup()
    {
        packetSender = Substitute.For<IPacketSender>();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        PlayerManager playerManager = new(null, null, null, NullLogger<PlayerManager>.Instance);
        worldEntityManager = new WorldEntityManager(packetSender, entityRegistry, null, playerManager, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        context = new AuthProcessorContext(CreatePlayer(), packetSender);
    }

    [TestMethod]
    public void CellsOfTestPositionsDiffer()
    {
        new AbsoluteEntityCell(positionA, 0).Should().NotBe(new AbsoluteEntityCell(positionB, 0));
    }

    [TestMethod]
    public async Task ItemSpawnedIntoContainerByClient_LeavesItsCell()
    {
        WorldEntity worldItem = AddWorldItem(positionA);
        InventoryEntity planter = AddContainer();

        await CreateEntitySpawnedByClientProcessor().Process(context, new EntitySpawnedByClient(ToInventoryItem(worldItem, planter.Id), true, false));

        IsInCell(worldItem.Id, positionA).Should().BeFalse();
        worldEntityManager.GetEntities(CellOf(positionA)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ItemDroppedInAnotherCell_IsOnlyInTheNewCell()
    {
        WorldEntity worldItem = AddWorldItem(positionA);
        WorldEntity droppedItem = CreateWorldItem(positionB, worldItem.Id);

        await CreateEntitySpawnedByClientProcessor().Process(context, new EntitySpawnedByClient(droppedItem, true, false));

        IsInCell(worldItem.Id, positionA).Should().BeFalse();
        worldEntityManager.GetEntities(CellOf(positionB)).Should().ContainSingle().Which.Should().BeSameAs(droppedItem);
    }

    [TestMethod]
    public async Task OtherWorldItemsStayInTheirCell()
    {
        WorldEntity untouchedItem = AddWorldItem(positionA);
        WorldEntity worldItem = AddWorldItem(positionA);
        InventoryEntity locker = AddContainer();

        await CreateEntitySpawnedByClientProcessor().Process(context, new EntitySpawnedByClient(ToInventoryItem(worldItem, locker.Id), true, false));

        worldEntityManager.GetEntities(CellOf(positionA)).Should().ContainSingle().Which.Should().BeSameAs(untouchedItem);
    }

    [TestMethod]
    public async Task ItemReparentedIntoContainer_LeavesItsCell()
    {
        WorldEntity worldItem = AddWorldItem(positionA);
        InventoryEntity locker = AddContainer();

        await new EntityReparentedProcessor(entityRegistry, worldEntityManager, NullLogger<EntityReparentedProcessor>.Instance)
              .Process(context, new EntityReparented(worldItem.Id, locker.Id));

        IsInCell(worldItem.Id, positionA).Should().BeFalse();
        locker.ChildEntities.Should().Contain(worldItem);
        await packetSender.Received(1).SendPacketToOthersAsync(Arg.Any<EntityReparented>(), Arg.Any<SessionId>());
    }

    [TestMethod]
    public async Task ItemReparentedIntoContainer_ClearsGhostEntryOfInventoryItem()
    {
        WorldEntity worldItem = AddWorldItem(positionA);
        InventoryEntity locker = AddContainer();
        InventoryEntity otherLocker = AddContainer();
        // Ghost: the registry already knows the item as an inventory item, but its old cell entry remained
        entityRegistry.AddOrUpdate(ToInventoryItem(worldItem, locker.Id));

        await new EntityReparentedProcessor(entityRegistry, worldEntityManager, NullLogger<EntityReparentedProcessor>.Instance)
              .Process(context, new EntityReparented(worldItem.Id, otherLocker.Id));

        IsInCell(worldItem.Id, positionA).Should().BeFalse();
    }

    [TestMethod]
    public async Task PickupItem_RemovesCellEntryEvenIfNotRecordedAsWorldEntity()
    {
        WorldEntity worldItem = AddWorldItem(positionA);
        InventoryEntity locker = AddContainer();
        entityRegistry.AddOrUpdate(ToInventoryItem(worldItem, locker.Id));

        await new PickupItemPacketProcessor(entityRegistry, worldEntityManager, new SimulationOwnershipData())
              .Process(context, new PickupItem(ToInventoryItem(worldItem, locker.Id)));

        IsInCell(worldItem.Id, positionA).Should().BeFalse();
    }

    [TestMethod]
    public async Task PickupItem_RemovesWorldItemFromItsCell()
    {
        WorldEntity worldItem = AddWorldItem(positionA);
        InventoryEntity inventory = AddContainer();

        await new PickupItemPacketProcessor(entityRegistry, worldEntityManager, new SimulationOwnershipData())
              .Process(context, new PickupItem(ToInventoryItem(worldItem, inventory.Id)));

        IsInCell(worldItem.Id, positionA).Should().BeFalse();
        entityRegistry.GetEntityById(worldItem.Id).Value.Should().BeOfType<InventoryItemEntity>();
    }

    [TestMethod]
    public void GetEntities_SkipsAndRemovesStaleEntries()
    {
        WorldEntity worldItem = AddWorldItem(positionA);
        WorldEntity destroyedItem = AddWorldItem(positionA);
        WorldEntity realItem = AddWorldItem(positionA);
        InventoryEntity locker = AddContainer();
        entityRegistry.AddOrUpdate(ToInventoryItem(worldItem, locker.Id));
        entityRegistry.RemoveEntity(destroyedItem.Id);

        worldEntityManager.GetEntities(CellOf(positionA)).Should().ContainSingle().Which.Should().BeSameAs(realItem);
        IsInCell(worldItem.Id, positionA).Should().BeFalse();
        IsInCell(destroyedItem.Id, positionA).Should().BeFalse();
    }

    [TestMethod]
    public void TryDestroyEntity_RemovesCellEntryEvenIfNotRecordedAsWorldEntity()
    {
        WorldEntity worldItem = AddWorldItem(positionA);
        InventoryEntity locker = AddContainer();
        entityRegistry.AddOrUpdate(ToInventoryItem(worldItem, locker.Id));

        worldEntityManager.TryDestroyEntity(worldItem.Id, out _).Should().BeTrue();

        IsInCell(worldItem.Id, positionA).Should().BeFalse();
    }

    [TestMethod]
    public void RegisteringInAnotherCell_MovesTheEntry()
    {
        WorldEntity worldItem = AddWorldItem(positionA);

        worldEntityManager.RegisterWorldEntityInCell(worldItem, CellOf(positionB));

        IsInCell(worldItem.Id, positionA).Should().BeFalse();
        IsInCell(worldItem.Id, positionB).Should().BeTrue();
    }

    [TestMethod]
    public void SetWorldEntities_ReplacesRegistrations()
    {
        WorldEntity oldItem = AddWorldItem(positionA);
        WorldEntity loadedItem = CreateWorldItem(positionB, new NitroxId());
        entityRegistry.AddOrUpdate(loadedItem);

        worldEntityManager.SetWorldEntities([loadedItem]);

        IsInCell(oldItem.Id, positionA).Should().BeFalse();
        worldEntityManager.GetEntities(CellOf(positionB)).Should().ContainSingle().Which.Should().BeSameAs(loadedItem);
        // The id index must be rebuilt too, so the loaded item can be removed by id
        worldEntityManager.StopTrackingEntity(loadedItem.Id);
        IsInCell(loadedItem.Id, positionB).Should().BeFalse();
    }

    [TestMethod]
    public void SetWorldEntities_SkipsRecordsInsideContainersOrGlobalRoots()
    {
        WorldEntity wreck = CreateWorldItem(positionA, new NitroxId());
        WorldEntity wreckPart = CreateWorldItem(positionA, new NitroxId());
        wreckPart.ParentId = wreck.Id;
        InventoryEntity locker = AddContainer();
        WorldEntity storedItem = CreateWorldItem(positionB, new NitroxId());
        storedItem.ParentId = locker.Id;
        GlobalRootEntity seabase = new(new NitroxTransform(positionB, NitroxQuaternion.Identity, NitroxVector3.One), 0, "base", false, new NitroxId(), NitroxTechType.None, null, null, []);
        WorldEntity itemInBase = CreateWorldItem(positionB, new NitroxId());
        itemInBase.ParentId = seabase.Id;
        foreach (Entity entity in new Entity[] { wreck, wreckPart, storedItem, seabase, itemInBase })
        {
            entityRegistry.AddOrUpdate(entity);
        }

        worldEntityManager.SetWorldEntities([wreck, wreckPart, storedItem, itemInBase]);

        worldEntityManager.GetEntities(CellOf(positionA)).Should().BeEquivalentTo([wreck, wreckPart], "batch hierarchies stay in their cell");
        worldEntityManager.GetEntities(CellOf(positionB)).Should().BeEmpty("items in a container or a base travel with it");
    }

    private EntitySpawnedByClientProcessor CreateEntitySpawnedByClientProcessor()
    {
        return new EntitySpawnedByClientProcessor(new PlayerManager(null, null, null, NullLogger<PlayerManager>.Instance), entityRegistry, worldEntityManager, null);
    }

    private WorldEntity AddWorldItem(NitroxVector3 position)
    {
        WorldEntity worldItem = CreateWorldItem(position, new NitroxId());
        entityRegistry.AddOrUpdate(worldItem);
        worldEntityManager.TrackEntityInTheWorld(worldItem);
        return worldItem;
    }

    private InventoryEntity AddContainer()
    {
        InventoryEntity container = new(0, new NitroxId(), new NitroxTechType("SmallLocker"), null, null, []);
        entityRegistry.AddOrUpdate(container);
        return container;
    }

    private static WorldEntity CreateWorldItem(NitroxVector3 position, NitroxId id)
    {
        NitroxTransform transform = new(position, NitroxQuaternion.Identity, NitroxVector3.One);
        return new WorldEntity(transform, 0, "titanium-class-id", false, id, new NitroxTechType("Titanium"), null, null, []);
    }

    private static InventoryItemEntity ToInventoryItem(WorldEntity worldItem, NitroxId containerId)
    {
        return new InventoryItemEntity(worldItem.Id, worldItem.ClassId, worldItem.TechType, null, containerId, []);
    }

    private static AbsoluteEntityCell CellOf(NitroxVector3 position) => new(position, 0);

    private bool IsInCell(NitroxId id, NitroxVector3 position)
    {
        return worldEntityManager.worldEntitiesByCell.TryGetValue(CellOf(position), out Dictionary<NitroxId, WorldEntity> entities) && entities.ContainsKey(id);
    }

    private static ServerPlayer CreatePlayer()
    {
        return new ServerPlayer(default, default, "Tester", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, default, null, default,
                          [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(), new Dictionary<string, PingInstancePreference>(), [], false, false);
    }
}
