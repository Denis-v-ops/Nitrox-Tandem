using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC D6: an item a player throws into a cell the partner hasn't loaded leaves the thrower's hands in
///     the partner's game too (L2). Before, the partner got nothing and kept it in the thrower's inventory (a thrown
///     flare stayed unlit there).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class DroppedOutOfSightTest
{
    private static readonly NitroxVector3 throwPosition = new(263, -60, 184);

    [TestMethod]
    public async Task PartnerWithoutTheCell_DropsTheirCopy_PartnerWithTheCell_GetsTheSpawn()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-DroppedOutOfSight");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        // Far away: Bob has no cells loaded
        NitroxId farFlare = await PutFlareInAlicesInventoryAsync(server, alice);
        bob.ClearReceived();
        alice.Send(new EntitySpawnedByClient(Thrown(farFlare), true));
        await bob.WaitForAsync<EntityDestroyed>(packet => packet.Id == farFlare);
        bob.ReceivedOf<SpawnEntities>().Should().NotContain(packet => packet.Entities.Any(entity => entity.Id == farFlare));
        server.EntityRegistry.TryGetEntityById(farFlare, out WorldEntity? stored).Should().BeTrue("the server keeps the thrown flare in the world");
        stored!.Transform.Position.Should().Be(throwPosition);

        // Near: Bob has the cell
        NitroxId nearFlare = await PutFlareInAlicesInventoryAsync(server, alice);
        AbsoluteEntityCell cell = Thrown(nearFlare).AbsoluteEntityCell;
        bob.Send(new CellVisibilityChanged(bob.SessionId, [cell], []));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));
        bob.ClearReceived();
        alice.Send(new EntitySpawnedByClient(Thrown(nearFlare), true));
        SpawnEntities spawn = await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == nearFlare));
        spawn.ForceRespawn.Should().BeTrue();
        await bob.AssertNotReceivedAsync<EntityDestroyed>(packet => packet.Id == nearFlare);
    }

    private static async Task<NitroxId> PutFlareInAlicesInventoryAsync(ServerHarness server, FakePlayer alice)
    {
        NitroxId id = new();
        alice.Send(new PickupItem(new InventoryItemEntity(id, "flare-class-id", new NitroxTechType("Flare"), null, alice.InitialSync.PlayerGameObjectId, [])));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.TryGetEntityById(id, out InventoryItemEntity _), "the flare is in Alice's inventory");
        return id;
    }

    private static WorldEntity Thrown(NitroxId id)
    {
        NitroxTransform transform = new(throwPosition, NitroxQuaternion.Identity, NitroxVector3.One);
        return new WorldEntity(transform, 0, "flare-class-id", false, id, new NitroxTechType("Flare"), null, null, []);
    }
}
