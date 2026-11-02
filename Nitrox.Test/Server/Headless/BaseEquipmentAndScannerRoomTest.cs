using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     Server halves of FIX_SPEC S2 and S10, with the packets the game sends for each step (see the comments). The game
///     halves (spawning, cameras, scanner UI, resource trackers) are the L3 scenarios in tools/scenarios/S2-*, S10-*.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class BaseEquipmentAndScannerRoomTest
{
    /// <summary>
    ///     S2 (#2259) and root cause B: a resync request, and taking an item out of a slot (EquipmentSlots.BroadcastUnequip
    ///     then Items.MovedIntoInventory), keep every item, across a restart.
    /// </summary>
    [TestMethod]
    public async Task ChargerBatteries_SurviveResyncUnequipAndRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-S2-Equipment");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        BuildEntity baseEntity = CreateBase();
        ModuleEntity charger = new(1, true, new NitroxTransform(new NitroxVector3(1, 2, 3), NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "charger-class-id", true,
                                   new NitroxId(), new NitroxTechType("BatteryCharger"), null, baseEntity.Id, []);
        InstalledModuleEntity kept = new("BatteryCharger1", "battery-class-id", new NitroxId(), new NitroxTechType("Battery"), new BatteryMetadata(80), charger.Id, []);
        InstalledModuleEntity taken = new("BatteryCharger2", "battery-class-id", new NitroxId(), new NitroxTechType("Battery"), new BatteryMetadata(40), charger.Id, []);
        charger.ChildEntities.Add(kept);
        charger.ChildEntities.Add(taken);
        baseEntity.ChildEntities.Add(charger);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);

        alice.Send(new BuildingResyncRequest());
        BuildingResync resync = await alice.WaitForAsync<BuildingResync>();
        resync.BuildEntities.Keys.Should().Contain(entity => entity.Id == baseEntity.Id);
        AssertParent(server, kept.Id, charger.Id);
        AssertParent(server, taken.Id, charger.Id);

        NitroxId aliceInventory = alice.InitialSync.PlayerGameObjectId;
        alice.Send(new EntityDestroyed(taken.Id));
        alice.Send(new EntitySpawnedByClient(new InventoryItemEntity(taken.Id, "battery-class-id", new NitroxTechType("Battery"), new BatteryMetadata(40), aliceInventory, []), true));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == taken.Id));
        AssertParent(server, taken.Id, aliceInventory);

        await server.RestartAsync();

        AssertParent(server, kept.Id, charger.Id);
        AssertParent(server, taken.Id, aliceInventory);
        server.EntityRegistry.TryGetEntityById(charger.Id, out ModuleEntity loadedCharger).Should().BeTrue();
        loadedCharger.ChildEntities.Should().ContainSingle().Which.Id.Should().Be(kept.Id);
    }

    /// <summary>
    ///     S2 (#2696) and S10 (#2410): scanner room upgrades (EntityReparented into the room), the scan target and docked
    ///     drones (MapRoomMetadata) and a drone taken out (PickupItem) reach the partner, survive a restart and are in a late
    ///     joiner's payload.
    /// </summary>
    [TestMethod]
    public async Task ScannerRoomState_ReachesPartner_SurvivesRestart_AndIsSentToLateJoiner()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-S10-ScannerRoom");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        BuildEntity baseEntity = CreateBase();
        NitroxId mapRoomId = baseEntity.Id.Increment();
        NitroxId takenCamera = mapRoomId.Increment();
        NitroxId dockedCamera = takenCamera.Increment();
        MapRoomEntity mapRoom = new(mapRoomId, baseEntity.Id, new NitroxInt3(0, 0, 0));
        baseEntity.ChildEntities.Add(mapRoom);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);
        NitroxId aliceInventory = alice.InitialSync.PlayerGameObjectId;

        // Two upgrades: spawned in Alice's inventory (console "item"), then moved into the room's storage
        NitroxId[] upgrades = [new(), new()];
        foreach (NitroxId upgrade in upgrades)
        {
            alice.Send(new PickupItem(new InventoryItemEntity(upgrade, "scan-range-class-id", new NitroxTechType("MapRoomUpgradeScanRange"), null, aliceInventory, [])));
            alice.Send(new EntityReparented(upgrade, mapRoomId));
        }
        // Scan target, and one drone taken out of its dock
        alice.Send(new EntityMetadataUpdate(mapRoomId, new MapRoomMetadata(new NitroxTechType("LimestoneChunk"), [takenCamera, dockedCamera])));
        alice.Send(new PickupItem(new InventoryItemEntity(takenCamera, "camera-class-id", new NitroxTechType("MapRoomCamera"), null, aliceInventory, [])));
        alice.Send(new EntityMetadataUpdate(mapRoomId, new MapRoomMetadata(new NitroxTechType("LimestoneChunk"), [dockedCamera])));

        EntityMetadataUpdate update = await bob.WaitForAsync<EntityMetadataUpdate>(packet => packet.Id == mapRoomId && packet.NewValue is MapRoomMetadata { DockedCameraIds.Count: 1 });
        ((MapRoomMetadata)update.NewValue).TypeToScan.Name.Should().Be("LimestoneChunk");
        await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == takenCamera));
        AssertScannerRoom(server.EntityRegistry, mapRoomId, upgrades, dockedCamera);

        await server.RestartAsync();

        AssertScannerRoom(server.EntityRegistry, mapRoomId, upgrades, dockedCamera);
        AssertParent(server, takenCamera, aliceInventory);

        FakePlayer carol = await server.JoinAsync("Carol");
        BuildEntity sentBase = carol.InitialSync.GlobalRootEntities.OfType<BuildEntity>().Should().ContainSingle(entity => entity.Id == baseEntity.Id).Subject;
        MapRoomEntity sentMapRoom = sentBase.ChildEntities.OfType<MapRoomEntity>().Should().ContainSingle().Subject;
        sentMapRoom.ChildEntities.Select(child => child.Id).Should().BeEquivalentTo(upgrades);
        sentMapRoom.Metadata.Should().BeOfType<MapRoomMetadata>().Which.DockedCameraIds.Should().Equal(dockedCamera);
    }

    /// <summary>
    ///     S10 (#2046, #2151): a broken outcrop in a cell the partner doesn't have loaded still reaches the partner, whose
    ///     scanner room keeps tracking resources of unloaded cells.
    /// </summary>
    [TestMethod]
    public async Task DestroyedOutcrop_ReachesPartnerWithoutTheCellLoaded()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-S10-Pings");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity outcrop = new(new NitroxTransform(new NitroxVector3(600, -20, 600), NitroxQuaternion.Identity, NitroxVector3.One), 0, "limestone-class-id", false, new NitroxId(),
                                  new NitroxTechType("LimestoneChunk"), null, null, []);
        server.EntityRegistry.AddOrUpdate(outcrop);
        server.WorldEntityManager.TrackEntityInTheWorld(outcrop);

        alice.Send(new EntityDestroyed(outcrop.Id));

        await bob.WaitForAsync<EntityDestroyed>(packet => packet.Id == outcrop.Id);
        server.EntityRegistry.TryGetEntityById(outcrop.Id, out Entity _).Should().BeFalse();
    }

    private static void AssertScannerRoom(EntityRegistry registry, NitroxId mapRoomId, NitroxId[] upgrades, NitroxId dockedCamera)
    {
        registry.TryGetEntityById(mapRoomId, out MapRoomEntity mapRoom).Should().BeTrue();
        mapRoom.ChildEntities.Select(child => child.Id).Should().BeEquivalentTo(upgrades);
        MapRoomMetadata metadata = mapRoom.Metadata.Should().BeOfType<MapRoomMetadata>().Subject;
        metadata.TypeToScan.Name.Should().Be("LimestoneChunk");
        metadata.DockedCameraIds.Should().Equal(dockedCamera);
    }

    private static void AssertParent(ServerHarness server, NitroxId id, NitroxId parentId)
    {
        server.EntityRegistry.TryGetEntityById(id, out Entity entity).Should().BeTrue($"the server must still have {id}");
        entity.ParentId.Should().Be(parentId);
    }

    private static BuildEntity CreateBase()
    {
        BuildEntity baseEntity = BuildEntity.MakeEmpty();
        baseEntity.Id = new NitroxId();
        baseEntity.Level = GlobalRootEntity.GLOBAL_ROOT_LEVEL;
        baseEntity.SpawnedByServer = true;
        baseEntity.Transform = new NitroxTransform(new NitroxVector3(60, -26, 207), NitroxQuaternion.Identity, NitroxVector3.One);
        baseEntity.BaseData = new BaseData
        {
            BaseShape = new NitroxInt3(1, 1, 1),
            CellOffset = new NitroxInt3(0, 0, 0),
            Anchor = new NitroxInt3(0, 0, 0),
            PreCompressionSize = 1,
            Faces = [1],
            Cells = [2],
            Links = [3],
            Masks = [4],
            IsGlass = [0]
        };
        return baseEntity;
    }
}
