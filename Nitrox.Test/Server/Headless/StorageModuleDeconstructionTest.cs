using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC B2, non-empty storage: a game deconstructs a module to the end with
///     <see cref="ModifyConstructedAmount" /> 0 (<c>Constructable_Construct_Patch.ConstructionAmountModified</c>). The game only
///     allows that for empty storage (<c>Constructable.CanDeconstruct</c>), so when the server still has items in the module,
///     that game's copy was wrong: the server keeps the module and its items and sends that game its copy back.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class StorageModuleDeconstructionTest
{
    [TestMethod]
    public async Task NonEmptyLocker_IsKept_AndSentBackToTheDeconstructingPlayer()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-B2-Storage");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        ModuleEntity locker = CreateModule("Locker", 0.4f);
        InventoryItemEntity titanium = new(new NitroxId(), "titanium-class-id", new NitroxTechType("Titanium"), null, locker.Id, []);
        locker.ChildEntities.Add(titanium);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(locker);
        ModuleEntity emptyLocker = CreateModule("Locker", 0.4f);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(emptyLocker);
        alice.ClearReceived();
        bob.ClearReceived();

        bob.Send(new ModifyConstructedAmount(locker.Id, 0f));
        // Bob deconstructs an empty locker right after: Alice gets that one, so she would have got the first by now
        bob.Send(new ModifyConstructedAmount(emptyLocker.Id, 0f));

        SpawnEntities respawn = await bob.WaitForAsync<SpawnEntities>(packet => packet.ForceRespawn && packet.Entities.Any(entity => entity.Id == locker.Id));
        Entity sentLocker = respawn.Entities.Single(entity => entity.Id == locker.Id);
        sentLocker.ChildEntities.Should().ContainSingle(entity => entity.Id == titanium.Id, "Bob gets the locker back with its item");
        await alice.WaitForAsync<ModifyConstructedAmount>(packet => packet.GhostId == emptyLocker.Id && packet.ConstructedAmount == 0f);
        alice.ReceivedOf<ModifyConstructedAmount>().Should().NotContain(packet => packet.GhostId == locker.Id, "the partner's locker stays");
        alice.ReceivedOf<SpawnEntities>().Should().NotContain(packet => packet.Entities.Any(entity => entity.Id == locker.Id), "only Bob's copy was wrong");

        server.EntityRegistry.TryGetEntityById(locker.Id, out ModuleEntity kept).Should().BeTrue("a locker with items isn't removed");
        kept.ConstructedAmount.Should().Be(0.4f);
        server.EntityRegistry.TryGetEntityById(titanium.Id, out InventoryItemEntity keptItem).Should().BeTrue();
        keptItem.ParentId.Should().Be(locker.Id);
        server.EntityRegistry.TryGetEntityById(emptyLocker.Id, out Entity _).Should().BeFalse("an empty locker is deconstructed as before");

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(titanium.Id, out InventoryItemEntity reloaded).Should().BeTrue();
        reloaded.ParentId.Should().Be(locker.Id);
    }

    [TestMethod]
    public async Task ChargerWithBattery_IsKept()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-B2-Charger");
        FakePlayer alice = await server.JoinAsync("Alice");
        ModuleEntity charger = CreateModule("BatteryCharger", 0.2f);
        InstalledModuleEntity battery = new("BatteryCharger1", "battery-class-id", new NitroxId(), new NitroxTechType("Battery"), null, charger.Id, []);
        charger.ChildEntities.Add(battery);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(charger);

        alice.Send(new ModifyConstructedAmount(charger.Id, 0f));

        await alice.WaitForAsync<SpawnEntities>(packet => packet.ForceRespawn && packet.Entities.Any(entity => entity.Id == charger.Id));
        server.EntityRegistry.TryGetEntityById(battery.Id, out Entity _).Should().BeTrue();
    }

    private static ModuleEntity CreateModule(string techType, float constructedAmount)
    {
        return new ModuleEntity(constructedAmount, true, new NitroxTransform(new NitroxVector3(60, -26, 207), NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL,
                                $"{techType}-class-id", true, new NitroxId(), new NitroxTechType(techType), null, null, []);
    }
}
