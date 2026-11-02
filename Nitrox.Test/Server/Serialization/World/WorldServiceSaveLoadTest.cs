using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.Serialization.World;

[TestClass]
public class WorldServiceSaveLoadTest
{
    [TestMethod]
    public void BaseWithChargersAndScannerRoomSurvivesSaveAndLoad()
    {
        // Shaped like the client sends it: BuildEntitySpawner.From, ModuleEntitySpawner.From and Items.GetEquipmentModuleEntities.
        // Slot names and class ids are only example strings.
        BuildEntity baseEntity = CreateBase();
        ModuleEntity batteryCharger = AddModule(baseEntity, "BatteryCharger", new NitroxVector3(1, 2, 3));
        AddEquipment(batteryCharger, "BatteryCharger1", "Battery", 73.5f);
        AddEquipment(batteryCharger, "BatteryCharger3", "Battery", 100f);
        ModuleEntity powerCellCharger = AddModule(baseEntity, "PowerCellCharger", new NitroxVector3(-4, 2, 0.25f));
        AddEquipment(powerCellCharger, "PowerCellCharger1", "PowerCell", 12.125f);
        MapRoomEntity scannerRoom = new(new NitroxId(), baseEntity.Id, new NitroxInt3(1, 0, 2));
        scannerRoom.ChildEntities.Add(new InventoryItemEntity(new NitroxId(), "scanner-upgrade-class-id", new NitroxTechType("MapRoomUpgradeScanRange"), null, scannerRoom.Id, []));
        baseEntity.ChildEntities.Add(scannerRoom);

        EntityRegistry loaded = SaveLoadRoundTrip.AssertSurvivesSaveAndLoad(baseEntity);

        loaded.TryGetEntityById(batteryCharger.Id, out ModuleEntity loadedCharger).Should().BeTrue();
        loadedCharger.ChildEntities.OfType<InstalledModuleEntity>().Select(module => module.Slot).Should().BeEquivalentTo("BatteryCharger1", "BatteryCharger3");
    }

    [TestMethod]
    public void ScannerRoomStateSurvivesSaveAndLoad()
    {
        // FIX_SPEC S2/S10: upgrades (InventoryItemEntity children), the shared scan target and the docked camera drones.
        BuildEntity baseEntity = CreateBase();
        ModuleEntity batteryCharger = AddModule(baseEntity, "BatteryCharger", new NitroxVector3(1, 2, 3));
        AddEquipment(batteryCharger, "BatteryCharger1", "Battery", 42f);
        NitroxId scannerRoomId = new();
        NitroxId dockedCameraId = scannerRoomId.Increment().Increment();
        MapRoomEntity scannerRoom = new(scannerRoomId, baseEntity.Id, new NitroxInt3(1, 0, 2))
        {
            Metadata = new MapRoomMetadata(new NitroxTechType("Titanium"), [dockedCameraId])
        };
        scannerRoom.ChildEntities.Add(new InventoryItemEntity(new NitroxId(), "scan-range-class-id", new NitroxTechType("MapRoomUpgradeScanRange"), null, scannerRoom.Id, []));
        scannerRoom.ChildEntities.Add(new InventoryItemEntity(new NitroxId(), "scan-range-class-id", new NitroxTechType("MapRoomUpgradeScanRange"), null, scannerRoom.Id, []));
        baseEntity.ChildEntities.Add(scannerRoom);
        MapRoomEntity idleScannerRoom = new(new NitroxId(), baseEntity.Id, new NitroxInt3(3, 0, 2))
        {
            Metadata = new MapRoomMetadata(new NitroxTechType("None"), [])
        };
        baseEntity.ChildEntities.Add(idleScannerRoom);

        EntityRegistry loaded = SaveLoadRoundTrip.AssertSurvivesSaveAndLoad(baseEntity);

        loaded.TryGetEntityById(scannerRoomId, out MapRoomEntity loadedScannerRoom).Should().BeTrue();
        loadedScannerRoom.ChildEntities.OfType<InventoryItemEntity>().Should().HaveCount(2);
        MapRoomMetadata metadata = loadedScannerRoom.Metadata.Should().BeOfType<MapRoomMetadata>().Subject;
        metadata.TypeToScan.Name.Should().Be("Titanium");
        metadata.DockedCameraIds.Should().Equal(dockedCameraId);
        loaded.TryGetEntityById(idleScannerRoom.Id, out MapRoomEntity loadedIdleRoom).Should().BeTrue();
        ((MapRoomMetadata)loadedIdleRoom.Metadata).DockedCameraIds.Should().BeEmpty();
    }

    [TestMethod]
    public void DestroyedCyclopsIsNotSaved()
    {
        // WorldEntityManager.GetPersistentGlobalRootEntities skips Cyclops wrecks on purpose.
        VehicleEntity wreck = new(null, 0, new NitroxTransform(new NitroxVector3(5, -100, 5), NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "cyclops-class-id", true, new NitroxId(),
                                  new NitroxTechType("Cyclops"), new CyclopsMetadata(false, false, false, false, 0, 0, true), null, []);

        SaveLoadRoundTrip.SaveAndLoad(wreck).TryGetEntityById(wreck.Id, out Entity _).Should().BeFalse();
        Invoking(() => SaveLoadRoundTrip.AssertSurvivesSaveAndLoad(wreck)).Should().Throw<Exception>("the helper must report entities that are not saved");
    }

    [TestMethod]
    public void AssertSameEntityComparesSubtypeMembers()
    {
        // Guards against the comparison only looking at the members declared on Entity.
        NitroxId id = new();
        InstalledModuleEntity expected = new("BatteryCharger1", "battery-class-id", id, new NitroxTechType("Battery"), new BatteryMetadata(50), new NitroxId(), []);
        InstalledModuleEntity otherSlot = new("BatteryCharger2", "battery-class-id", id, new NitroxTechType("Battery"), new BatteryMetadata(50), expected.ParentId, []);
        InstalledModuleEntity otherCharge = new("BatteryCharger1", "battery-class-id", id, new NitroxTechType("Battery"), new BatteryMetadata(49), expected.ParentId, []);

        SaveLoadRoundTrip.AssertSameEntity(expected, expected);
        Invoking(() => SaveLoadRoundTrip.AssertSameEntity(otherSlot, expected)).Should().Throw<Exception>();
        Invoking(() => SaveLoadRoundTrip.AssertSameEntity(otherCharge, expected)).Should().Throw<Exception>();
    }

    private static BuildEntity CreateBase()
    {
        BuildEntity baseEntity = BuildEntity.MakeEmpty();
        baseEntity.Id = new NitroxId();
        baseEntity.Level = GlobalRootEntity.GLOBAL_ROOT_LEVEL;
        // The client sends false, but the constructor JSON loads BuildEntity with always sets it to true (same pattern as
        // the ModuleEntity constructor, whose remarks say it's so the entity gets simulation locks).
        baseEntity.SpawnedByServer = true;
        baseEntity.Transform = new NitroxTransform(new NitroxVector3(120.5f, -35.25f, -480), new NitroxQuaternion(0, 0.7071068f, 0, 0.7071068f), NitroxVector3.One);
        baseEntity.BaseData = new BaseData
        {
            BaseShape = new NitroxInt3(3, 1, 3),
            CellOffset = new NitroxInt3(-1, 0, -1),
            Anchor = new NitroxInt3(0, 0, 0),
            PreCompressionSize = 9,
            Faces = [1, 2, 3, 4],
            Cells = [5, 6, 7],
            Links = [8, 9],
            Masks = [10],
            IsGlass = [0, 1]
        };
        return baseEntity;
    }

    private static ModuleEntity AddModule(BuildEntity baseEntity, string techType, NitroxVector3 localPosition)
    {
        ModuleEntity module = new(1, true, new NitroxTransform(localPosition, NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, $"{techType}-class-id", true, new NitroxId(),
                                  new NitroxTechType(techType), null, baseEntity.Id, []);
        baseEntity.ChildEntities.Add(module);
        return module;
    }

    private static void AddEquipment(ModuleEntity owner, string slot, string techType, float charge)
    {
        owner.ChildEntities.Add(new InstalledModuleEntity(slot, $"{techType}-class-id", new NitroxId(), new NitroxTechType(techType), new BatteryMetadata(charge), owner.Id, []));
    }
}
