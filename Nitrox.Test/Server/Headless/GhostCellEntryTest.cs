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
///     S7 step 1 (#2691): an item that went from the world into a container must not stay in its old cell, otherwise every
///     player who loads that cell respawns it there (floating resources that keep coming back).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class GhostCellEntryTest
{
    private static readonly NitroxVector3 itemPosition = new(52, -31, 47);
    private static readonly AbsoluteEntityCell itemCell = new(itemPosition, 0);

    private static ServerHarness server = null!;

    [ClassInitialize]
    public static async Task StartServer(TestContext context)
    {
        server = await ServerHarness.StartAsync("L2-GhostCell");
    }

    [ClassCleanup]
    public static async Task StopServer()
    {
        await server.DisposeAsync();
    }

    [TestCleanup]
    public async Task DisconnectPlayers()
    {
        await server.DisconnectPlayersAsync();
    }

    [TestMethod]
    public async Task ItemReparentedIntoContainer_IsNotResentAsWorldEntityOfItsOldCell()
    {
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        (WorldEntity locker, InventoryEntity storage, WorldEntity item) = await PutWorldItemIntoLockerAsync(alice, bob);

        await AssertItemOnlyInsideLockerAsync(bob, locker, storage, item);
    }

    /// <summary>
    ///     The usual way in game: pick the item up (it becomes an inventory item), then put it into the locker. The server
    ///     rebuilds its cell lists from the saved records when it loads (WorldService), so a restart is a separate path.
    /// </summary>
    [TestMethod]
    public async Task ItemPickedUpThenStored_IsNotResentAsWorldEntityAfterRestart()
    {
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        (WorldEntity locker, InventoryEntity storage, WorldEntity worldItem) = await SpawnLockerAndWorldItemAsync(alice);
        alice.Send(new PickupItem(new InventoryItemEntity(worldItem.Id, worldItem.ClassId, worldItem.TechType, null, alice.InitialSync.PlayerGameObjectId, [])));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == worldItem.Id));
        alice.Send(new EntityReparented(worldItem.Id, storage.Id));
        await bob.WaitForAsync<EntityReparented>(packet => packet.Id == worldItem.Id);

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(worldItem.Id, out InventoryItemEntity reloadedItem).Should().BeTrue();
        reloadedItem.ParentId.Should().Be(storage.Id);
        await AssertItemOnlyInsideLockerAsync(await server.JoinAsync("Bob"), locker, storage, worldItem);
    }

    /// <summary>
    ///     Same as <see cref="ItemReparentedIntoContainer_IsNotResentAsWorldEntityOfItsOldCell" />, across a restart.
    /// </summary>
    /// <remarks>
    ///     Fails today (found with this harness, 2026-11-02): on load, <c>WorldService</c> registers every
    ///     <see cref="WorldEntity" /> record that isn't a global root in a cell (<c>SetWorldEntities</c>), parented ones
    ///     included. The item still is a <see cref="WorldEntity" /> (only its parent changed), so it's back in its old cell.
    /// </remarks>
    [TestMethod]
    [Ignore("Known bug: WorldService.SetWorldEntities puts parented world entities back into cells on load. See FIX_SPEC Log 2026-11-02 L2.")]
    public async Task ItemReparentedIntoContainer_IsNotResentAsWorldEntityAfterRestart()
    {
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        (WorldEntity locker, InventoryEntity storage, WorldEntity item) = await PutWorldItemIntoLockerAsync(alice, bob);

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(item.Id, out Entity reloadedItem).Should().BeTrue();
        reloadedItem.ParentId.Should().Be(storage.Id);
        await AssertItemOnlyInsideLockerAsync(await server.JoinAsync("Bob"), locker, storage, item);
    }

    private static async Task<(WorldEntity locker, InventoryEntity storage, WorldEntity item)> PutWorldItemIntoLockerAsync(FakePlayer alice, FakePlayer bob)
    {
        (WorldEntity locker, InventoryEntity storage, WorldEntity item) = await SpawnLockerAndWorldItemAsync(alice);
        // The item goes from the world straight into the locker, its record stays a WorldEntity
        alice.Send(new EntityReparented(item.Id, storage.Id));
        await bob.WaitForAsync<EntityReparented>(packet => packet.Id == item.Id);
        return (locker, storage, item);
    }

    /// <summary>
    ///     Alice builds a locker and drops an item next to it, in the same cell.
    /// </summary>
    private static async Task<(WorldEntity locker, InventoryEntity storage, WorldEntity item)> SpawnLockerAndWorldItemAsync(FakePlayer alice)
    {
        NitroxId lockerId = new();
        InventoryEntity storage = new(0, new NitroxId(), new NitroxTechType("SmallLocker"), null, lockerId, []);
        WorldEntity locker = new(new NitroxTransform(itemPosition + new NitroxVector3(2, 0, 0), NitroxQuaternion.Identity, NitroxVector3.One), 0, "small-locker-class-id", false, lockerId,
                                 new NitroxTechType("SmallLocker"), null, null, [storage]);
        WorldEntity item = new(new NitroxTransform(itemPosition, NitroxQuaternion.Identity, NitroxVector3.One), 0, "titanium-class-id", false, new NitroxId(),
                               new NitroxTechType("Titanium"), null, null, []);

        alice.Send(new EntitySpawnedByClient(locker, false, false));
        alice.Send(new EntitySpawnedByClient(item, false, false));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.TryGetEntityById(item.Id, out WorldEntity _), "the server knows the dropped item");
        return (locker, storage, item);
    }

    /// <summary>
    ///     The item reaches the player inside the locker, and not as an entity lying in its cell.
    /// </summary>
    private static async Task AssertItemOnlyInsideLockerAsync(FakePlayer player, WorldEntity locker, InventoryEntity storage, Entity item)
    {
        List<Entity> cellEntries = await LoadCellAsync(player);
        cellEntries.Should().NotContain(entity => entity.Id == item.Id, "the item is in the locker now, not lying in the world");
        cellEntries.Should().ContainSingle(entity => entity.Id == locker.Id)
                   .Which.ChildEntities.Should().ContainSingle(entity => entity.Id == storage.Id)
                   .Which.ChildEntities.Should().ContainSingle(entity => entity.Id == item.Id);
    }

    /// <summary>
    ///     Bob reports the item's cell as visible. The server always answers with the cell's entities (possibly none).
    /// </summary>
    private static async Task<List<Entity>> LoadCellAsync(FakePlayer player)
    {
        player.Send(new CellVisibilityChanged(player.SessionId, [itemCell], []));
        SpawnEntities reply = await player.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(itemCell));
        return reply.Entities;
    }
}
