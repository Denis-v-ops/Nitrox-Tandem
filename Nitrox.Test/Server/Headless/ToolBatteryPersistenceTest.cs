using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     S8 (#2668), server side: a tool handed from one player to the other keeps exactly the batteries it had, also after
///     a restart. The game-side half (the pickup not inventing a battery) is the L3 scenario S8-EmptyToolNoDupe.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class ToolBatteryPersistenceTest
{
    private static readonly NitroxVector3 dropPosition = new(-30, -12, 80);

    [TestMethod]
    public async Task ToolsPickedUpByPartner_KeepTheirBatteryCountAcrossRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-ToolBattery");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId emptyId = new();
        NitroxId chargedId = new();
        NitroxId batteryId = new();
        int batteriesBefore = server.EntityRegistry.GetEntities<InstalledBatteryEntity>().Count;

        // Alice drops a flashlight without battery and one with a battery, Bob picks both up
        alice.Send(new EntitySpawnedByClient(Flashlight(emptyId, null), false, false));
        alice.Send(new EntitySpawnedByClient(Flashlight(chargedId, Battery(batteryId, chargedId)), false, false));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.TryGetEntityById(chargedId, out WorldEntity _), "the server knows both dropped flashlights");
        NitroxId bobInventory = bob.InitialSync.PlayerGameObjectId;
        bob.Send(new PickupItem(new InventoryItemEntity(emptyId, "flashlight-class-id", new NitroxTechType("Flashlight"), null, bobInventory, [])));
        bob.Send(new PickupItem(new InventoryItemEntity(chargedId, "flashlight-class-id", new NitroxTechType("Flashlight"), null, bobInventory, [Battery(batteryId, chargedId)])));
        await alice.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == chargedId) && packet.ForceRespawn);

        AssertBatteries(server, emptyId, chargedId, batteryId, bobInventory, batteriesBefore + 1);

        await server.RestartAsync();

        AssertBatteries(server, emptyId, chargedId, batteryId, bobInventory, batteriesBefore + 1);
        File.ReadAllText(Path.Combine(server.SavePath, "EntityData.json")).Should().Contain(batteryId.ToString());
    }

    private static void AssertBatteries(ServerHarness server, NitroxId emptyId, NitroxId chargedId, NitroxId batteryId, NitroxId inventoryId, int expectedBatteryCount)
    {
        server.EntityRegistry.TryGetEntityById(emptyId, out InventoryItemEntity empty).Should().BeTrue();
        empty.ParentId.Should().Be(inventoryId);
        empty.ChildEntities.Should().BeEmpty("the flashlight had no battery");

        server.EntityRegistry.TryGetEntityById(chargedId, out InventoryItemEntity charged).Should().BeTrue();
        charged.ParentId.Should().Be(inventoryId);
        charged.ChildEntities.Should().ContainSingle().Which.Id.Should().Be(batteryId);

        server.EntityRegistry.GetEntities<InstalledBatteryEntity>().Should().HaveCount(expectedBatteryCount, "no battery was added or lost");
    }

    private static WorldEntity Flashlight(NitroxId id, Entity? battery)
    {
        NitroxTransform transform = new(dropPosition, NitroxQuaternion.Identity, NitroxVector3.One);
        return new WorldEntity(transform, 0, "flashlight-class-id", false, id, new NitroxTechType("Flashlight"), null, null, battery == null ? [] : [battery]);
    }

    private static InstalledBatteryEntity Battery(NitroxId id, NitroxId toolId) => new(0, id, new NitroxTechType("Battery"), null, toolId, []);
}
