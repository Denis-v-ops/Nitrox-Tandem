using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC T3: handing an item over goes through the server, which moves it from the giver's inventory into
///     the target's and tells everyone, or refuses and changes nothing (L2, real server and protocol). Without
///     <c>ItemGiveRequestProcessor</c> the default processor would only relay the request and nothing would move.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class ItemGiveTest
{
    private static readonly NitroxVector3 spot = new(-60, -6, 380);

    [TestMethod]
    public async Task Give_MovesTheToolAndItsBattery_ForEveryone_AndSurvivesRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-ItemGive");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await MoveAsync(server, alice, spot);
        await MoveAsync(server, bob, spot + new NitroxVector3(1.5f, 0, 0));
        NitroxId aliceInventory = alice.InitialSync.PlayerGameObjectId;
        NitroxId bobInventory = bob.InitialSync.PlayerGameObjectId;
        NitroxId flashlightId = new();
        NitroxId batteryId = new();
        int batteriesBefore = server.EntityRegistry.GetEntities<InstalledBatteryEntity>().Count;
        await PickUpAsync(server, alice, bob, Flashlight(flashlightId, aliceInventory, batteryId));

        alice.Send(new ItemGiveRequest(flashlightId, bob.SessionId));

        // Both games get the move: the giver's moves it into its copy of Bob's inventory, Bob's into his own
        (await bob.WaitForAsync<EntityReparented>(packet => packet.Id == flashlightId)).NewParentId.Should().Be(bobInventory);
        (await alice.WaitForAsync<EntityReparented>(packet => packet.Id == flashlightId)).NewParentId.Should().Be(bobInventory);
        AssertFlashlightUnder(server, bobInventory, flashlightId, batteryId, batteriesBefore + 1);

        // A late joiner spawns it in Bob's inventory
        FakePlayer carol = await server.JoinAsync("Carol");
        // (New players start in the escape pod, so their entities are children of the pod's)
        Find(carol.InitialSync.GlobalRootEntities, bobInventory).ChildEntities.Should().ContainSingle(child => child.Id == flashlightId)
                   .Which.ChildEntities.Should().ContainSingle(child => child.Id == batteryId);
        Find(carol.InitialSync.GlobalRootEntities, aliceInventory).ChildEntities.Should().NotContain(child => child.Id == flashlightId);

        // Same id after save, stop and load (the server's real save code), no battery made or lost
        await server.RestartAsync();
        AssertFlashlightUnder(server, bobInventory, flashlightId, batteryId, batteriesBefore + 1);
        FakePlayer bobAgain = await server.JoinAsync("Bob");
        Find(bobAgain.InitialSync.GlobalRootEntities, bobInventory).ChildEntities.Should().ContainSingle(child => child.Id == flashlightId);
    }

    [TestMethod]
    public async Task Give_IsRefused_AndNothingMoves_WhenTheChecksFail()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-ItemGiveRefused");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        await MoveAsync(server, alice, spot);
        await MoveAsync(server, bob, spot + new NitroxVector3(1.5f, 0, 0));
        await MoveAsync(server, carol, spot + new NitroxVector3(10, 0, 0));
        NitroxId aliceInventory = alice.InitialSync.PlayerGameObjectId;
        NitroxId bobInventory = bob.InitialSync.PlayerGameObjectId;
        PlayerManager playerManager = server.Get<PlayerManager>();
        playerManager.TryGetPlayerByName("Alice", out ServerPlayer? aliceOnServer).Should().BeTrue();
        playerManager.TryGetPlayerByName("Bob", out ServerPlayer? bobOnServer).Should().BeTrue();

        NitroxId tankId = new();
        NitroxId knifeId = new();
        NitroxId bobsKnifeId = new();
        NitroxId worldItemId = new();
        await PickUpAsync(server, alice, bob, Item(tankId, "Tank", aliceInventory));
        await PickUpAsync(server, alice, bob, Item(knifeId, "Knife", aliceInventory));
        await PickUpAsync(server, bob, alice, Item(bobsKnifeId, "Knife", bobInventory));
        alice.Send(new EntitySpawnedByClient(new WorldEntity(new NitroxTransform(spot, NitroxQuaternion.Identity, NitroxVector3.One), 0, "titanium-class-id", false, worldItemId,
                                                             new NitroxTechType("Titanium"), null, null, []), false, false));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.TryGetEntityById(worldItemId, out WorldEntity _), "the server knows the world item");
        aliceOnServer!.EquippedItems["Tank"] = tankId;

        // Each refused give is followed by an allowed one of a sentinel item: packets from Alice are processed in order, so a
        // refused give that got through would reach Bob before the sentinel's move.
        async Task AssertRefusedAsync(ItemGiveRequest refused, string because)
        {
            NitroxId sentinelId = new();
            await PickUpAsync(server, alice, bob, Item(sentinelId, "Titanium", aliceInventory));
            alice.Send(refused);
            alice.Send(new ItemGiveRequest(sentinelId, bob.SessionId));
            EntityReparented first = await bob.WaitForAsync<EntityReparented>();
            first.Id.Should().Be(sentinelId, because);
            bob.ClearReceived();
            alice.ClearReceived();
        }

        await AssertRefusedAsync(new ItemGiveRequest(tankId, bob.SessionId), "the tank is equipped");
        await AssertRefusedAsync(new ItemGiveRequest(bobsKnifeId, bob.SessionId), "Alice can't give what Bob carries");
        await AssertRefusedAsync(new ItemGiveRequest(worldItemId, bob.SessionId), "a world item isn't in anyone's inventory");
        await AssertRefusedAsync(new ItemGiveRequest(new NitroxId(), bob.SessionId), "the server knows no such item");
        await AssertRefusedAsync(new ItemGiveRequest(knifeId, carol.SessionId), "Carol is 10 m away");
        await AssertRefusedAsync(new ItemGiveRequest(knifeId, alice.SessionId), "nobody gives to themselves");
        await AssertRefusedAsync(new ItemGiveRequest(knifeId, 999), "nobody has session 999");
        // No sentinel while one of them is down: every give between them is refused then (the L1 test covers the rule)
        bobOnServer!.DownedUntil = 1000;
        alice.Send(new ItemGiveRequest(knifeId, bob.SessionId));
        await bob.AssertNotReceivedAsync<EntityReparented>(quietPeriod: TimeSpan.FromSeconds(1));
        bobOnServer.DownedUntil = null;
        aliceOnServer.DownedUntil = 1000;
        alice.Send(new ItemGiveRequest(knifeId, bob.SessionId));
        await bob.AssertNotReceivedAsync<EntityReparented>(quietPeriod: TimeSpan.FromSeconds(1));
        aliceOnServer.DownedUntil = null;

        ParentOf(server, tankId).Should().Be(aliceInventory);
        ParentOf(server, knifeId).Should().Be(aliceInventory);
        ParentOf(server, bobsKnifeId).Should().Be(bobInventory);
        ParentOf(server, worldItemId).Should().BeNull();
        carol.ReceivedOf<EntityReparented>().Should().OnlyContain(packet => packet.NewParentId == bobInventory, "Carol saw only the sentinels move");
        carol.ReceivedOf<ItemGiveRequest>().Should().BeEmpty("the request isn't relayed");

        // Unequipped, the tank can be given
        aliceOnServer.EquippedItems.Clear();
        alice.Send(new ItemGiveRequest(tankId, bob.SessionId));
        (await bob.WaitForAsync<EntityReparented>(packet => packet.Id == tankId)).NewParentId.Should().Be(bobInventory);
        ParentOf(server, tankId).Should().Be(bobInventory);
    }

    private static Entity Find(IEnumerable<Entity> roots, NitroxId id)
    {
        Stack<Entity> pending = new(roots);
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            if (entity.Id == id)
            {
                return entity;
            }
            foreach (Entity child in entity.ChildEntities)
            {
                pending.Push(child);
            }
        }
        throw new AssertFailedException($"No entity {id} in the join payload");
    }

    private static NitroxId? ParentOf(ServerHarness server, NitroxId id)
    {
        server.EntityRegistry.TryGetEntityById(id, out Entity entity).Should().BeTrue();
        return entity.ParentId;
    }

    private static void AssertFlashlightUnder(ServerHarness server, NitroxId inventoryId, NitroxId flashlightId, NitroxId batteryId, int expectedBatteryCount)
    {
        server.EntityRegistry.TryGetEntityById(flashlightId, out InventoryItemEntity flashlight).Should().BeTrue("the item keeps its id");
        flashlight.ParentId.Should().Be(inventoryId);
        flashlight.ChildEntities.Should().ContainSingle().Which.Id.Should().Be(batteryId, "the battery moves with the tool");
        server.EntityRegistry.TryGetEntityById(batteryId, out InstalledBatteryEntity battery).Should().BeTrue();
        battery.ParentId.Should().Be(flashlightId);
        server.EntityRegistry.TryGetEntityById(inventoryId, out PlayerEntity player).Should().BeTrue();
        player.ChildEntities.Should().ContainSingle(child => child.Id == flashlightId);
        server.EntityRegistry.GetEntities<InstalledBatteryEntity>().Should().HaveCount(expectedBatteryCount, "no battery was added or lost");
    }

    /// <summary>
    ///     What a game sends when its player picks something up (<c>Items.PickedUp</c>); the partner gets the spawn.
    /// </summary>
    private static async Task PickUpAsync(ServerHarness server, FakePlayer picker, FakePlayer partner, InventoryItemEntity item)
    {
        picker.Send(new PickupItem(item));
        await partner.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == item.Id));
        server.EntityRegistry.TryGetEntityById(item.Id, out InventoryItemEntity _).Should().BeTrue();
    }

    private static InventoryItemEntity Item(NitroxId id, string techType, NitroxId inventoryId) => new(id, $"{techType}-class-id", new NitroxTechType(techType), null, inventoryId, []);

    private static InventoryItemEntity Flashlight(NitroxId id, NitroxId inventoryId, NitroxId batteryId)
    {
        return new InventoryItemEntity(id, "flashlight-class-id", new NitroxTechType("Flashlight"), null, inventoryId, [new InstalledBatteryEntity(0, batteryId, new NitroxTechType("Battery"), null, id, [])]);
    }

    private static async Task MoveAsync(ServerHarness server, FakePlayer player, NitroxVector3 position)
    {
        player.Send(new PlayerMovement(player.SessionId, position, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName(player.Name, out ServerPlayer? p) && p.Position == position,
                                           $"the server has {player.Name}'s position");
    }
}
