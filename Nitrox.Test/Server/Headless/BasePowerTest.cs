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
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC B3 (#1811), the server side of base power: the charge the lock holder's game sends
///     (<c>BasePowerSync</c>) is stored, relayed to the partner, saved and sent to a late joiner; a value from a game
///     without the lock is ignored. The game side (spawning with the stored value, the nuclear reactor's rods) is the L3
///     scenario tools/scenarios/B3-BasePowerPersists.ps1.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class BasePowerTest
{
    [TestMethod]
    public async Task LockHoldersPower_IsStored_Relayed_AndSentToLateJoiner()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-B3-BasePower");
        BuildEntity baseEntity = CreateBase();
        InteriorPieceEntity bioReactor = new(new NitroxBaseFace(new NitroxInt3(0, 0, 0), 4), new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One),
                                             GlobalRootEntity.GLOBAL_ROOT_LEVEL, "bioreactor-class-id", true, new NitroxId(), new NitroxTechType("BaseBioReactor"), null, baseEntity.Id, []);
        baseEntity.ChildEntities.Add(bioReactor);
        // Thermal plants stand alone on a vent: a global root of their own, with their own lock
        ModuleEntity thermalPlant = new(1, false, new NitroxTransform(new NitroxVector3(80, -30, 200), NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL,
                                        "thermal-class-id", true, new NitroxId(), new NitroxTechType("ThermalPlant"), new ThermalPlantMetadata(40f, null), null, []);
        // Before anyone joins, so the first joiner gets the locks (EntitySimulation.AssignGlobalRootEntitiesAndGetData), as in game
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(thermalPlant);
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        // Bob's game doesn't hold the lock: its own guess is ignored. His temperature rise is still news (anyone may send it).
        bob.Send(new EntityMetadataUpdate(bioReactor.Id, new PowerSourceMetadata(10f)));
        bob.Send(new EntityMetadataUpdate(thermalPlant.Id, new ThermalPlantMetadata(75f, 5f)));
        EntityMetadataUpdate fromBob = await alice.WaitForAsync<EntityMetadataUpdate>(packet => packet.Id == thermalPlant.Id);
        fromBob.NewValue.Should().BeOfType<ThermalPlantMetadata>().Which.Power.Should().BeNull("Bob doesn't hold the plant's lock");
        alice.ReceivedOf<EntityMetadataUpdate>().Should().NotContain(packet => packet.Id == bioReactor.Id, "Bob doesn't hold the base's lock");
        server.EntityRegistry.GetEntityById(bioReactor.Id).Value.Metadata.Should().BeNull();
        ThermalPlantMetadata thermal = (ThermalPlantMetadata)server.EntityRegistry.GetEntityById(thermalPlant.Id).Value.Metadata;
        thermal.Temperature.Should().Be(75f);
        thermal.Power.Should().BeNull();

        // Alice's game holds both locks: her values are stored and reach Bob. Her lower temperature doesn't lower the stored one.
        alice.Send(new EntityMetadataUpdate(bioReactor.Id, new PowerSourceMetadata(123.5f)));
        alice.Send(new EntityMetadataUpdate(thermalPlant.Id, new ThermalPlantMetadata(70f, 30f)));
        EntityMetadataUpdate reactorUpdate = await bob.WaitForAsync<EntityMetadataUpdate>(packet => packet.Id == bioReactor.Id);
        reactorUpdate.NewValue.Should().BeOfType<PowerSourceMetadata>().Which.Power.Should().Be(123.5f);
        EntityMetadataUpdate thermalUpdate = await bob.WaitForAsync<EntityMetadataUpdate>(packet => packet.Id == thermalPlant.Id);
        thermalUpdate.NewValue.Should().BeEquivalentTo(new ThermalPlantMetadata(75f, 30f));
        // A temperature-only update keeps the stored power
        bob.Send(new EntityMetadataUpdate(thermalPlant.Id, new ThermalPlantMetadata(76f, null)));
        await alice.WaitForAsync<EntityMetadataUpdate>(packet => packet.Id == thermalPlant.Id && ((ThermalPlantMetadata)packet.NewValue).Temperature == 76f);
        AssertStored(server, bioReactor.Id, thermalPlant.Id, 76f);

        await server.RestartAsync();

        AssertStored(server, bioReactor.Id, thermalPlant.Id, 76f);
        FakePlayer carol = await server.JoinAsync("Carol");
        BuildEntity sentBase = carol.InitialSync.GlobalRootEntities.OfType<BuildEntity>().Should().ContainSingle(entity => entity.Id == baseEntity.Id).Subject;
        sentBase.ChildEntities.Should().ContainSingle(entity => entity.Id == bioReactor.Id).Which.Metadata.Should().BeEquivalentTo(new PowerSourceMetadata(123.5f));
        carol.InitialSync.GlobalRootEntities.Should().ContainSingle(entity => entity.Id == thermalPlant.Id).Which.Metadata.Should().BeEquivalentTo(new ThermalPlantMetadata(76f, 30f));
    }

    /// <summary>
    ///     What the producer's game sends when a nuclear reactor rod runs out (<c>BaseNuclearReactor.ProducePower</c>, made
    ///     producer-only by <c>BaseNuclearReactor_ProducePower_Patch</c>): <c>EntityDestroyed</c> for the rod
    ///     (<c>Equipment_RemoveItem_Patch</c>), <c>PickupItem</c> for the new depleted rod into the player's inventory
    ///     (<c>Pickupable_Pickup_Patch</c>, from <c>AddDepletedRodToEquipmentAsync</c>'s <c>Pickup</c>), then
    ///     <c>EntitySpawnedByClient</c> into the slot (<c>Equipment_AddItem_Patch</c>). The server ends with one depleted rod
    ///     in the slot and nothing in the player's inventory, also after a restart.
    /// </summary>
    [TestMethod]
    public async Task DepletedRod_EndsUpInTheReactorSlotOnce()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-B3-ReactorRod");
        BuildEntity baseEntity = CreateBase();
        InteriorPieceEntity reactor = new(new NitroxBaseFace(new NitroxInt3(0, 0, 0), 4), new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One),
                                          GlobalRootEntity.GLOBAL_ROOT_LEVEL, "nuclear-class-id", true, new NitroxId(), new NitroxTechType("BaseNuclearReactor"), null, baseEntity.Id, []);
        InstalledModuleEntity rod = new("NuclearReactor1", "rod-class-id", new NitroxId(), new NitroxTechType("ReactorRod"), null, reactor.Id, []);
        InstalledModuleEntity otherRod = new("NuclearReactor2", "rod-class-id", new NitroxId(), new NitroxTechType("ReactorRod"), null, reactor.Id, []);
        reactor.ChildEntities.Add(rod);
        reactor.ChildEntities.Add(otherRod);
        baseEntity.ChildEntities.Add(reactor);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId depletedId = new();

        alice.Send(new EntityDestroyed(rod.Id));
        alice.Send(new PickupItem(new InventoryItemEntity(depletedId, "depleted-class-id", new NitroxTechType("DepletedReactorRod"), null, alice.InitialSync.PlayerGameObjectId, [])));
        alice.Send(new EntitySpawnedByClient(new InstalledModuleEntity("NuclearReactor1", "depleted-class-id", depletedId, new NitroxTechType("DepletedReactorRod"), null, reactor.Id, []), true));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == depletedId));
        AssertRods(server, reactor.Id, rod.Id, otherRod.Id, depletedId, alice.InitialSync.PlayerGameObjectId);

        await server.RestartAsync();

        AssertRods(server, reactor.Id, rod.Id, otherRod.Id, depletedId, alice.InitialSync.PlayerGameObjectId);
    }

    private static void AssertStored(ServerHarness server, NitroxId bioReactorId, NitroxId thermalPlantId, float temperature)
    {
        server.EntityRegistry.GetEntityById(bioReactorId).Value.Metadata.Should().BeEquivalentTo(new PowerSourceMetadata(123.5f));
        server.EntityRegistry.GetEntityById(thermalPlantId).Value.Metadata.Should().BeEquivalentTo(new ThermalPlantMetadata(temperature, 30f));
    }

    private static void AssertRods(ServerHarness server, NitroxId reactorId, NitroxId usedRodId, NitroxId otherRodId, NitroxId depletedId, NitroxId playerId)
    {
        server.EntityRegistry.TryGetEntityById(usedRodId, out Entity _).Should().BeFalse("the used-up rod is gone");
        server.EntityRegistry.TryGetEntityById(reactorId, out InteriorPieceEntity reactor).Should().BeTrue();
        reactor.ChildEntities.Select(child => child.Id).Should().BeEquivalentTo([otherRodId, depletedId]);
        reactor.ChildEntities.OfType<InstalledModuleEntity>().Should().ContainSingle(child => child.Id == depletedId).Which.Slot.Should().Be("NuclearReactor1");
        server.EntityRegistry.GetEntityById(playerId).Value.ChildEntities.Should().NotContain(child => child.Id == depletedId, "the depleted rod went on into the slot");
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
