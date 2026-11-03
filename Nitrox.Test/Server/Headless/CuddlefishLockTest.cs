using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC P1, server side. The cuddlefish follows the player whose game simulates it. Playing with it or
///     giving it a command asks for an exclusive lock, which takes it from the partner's normal lock; afterwards the
///     player drops back to a normal lock and keeps it, so the partner can't take it with a normal request ("last
///     interaction wins", no "nearest player" handover), only by playing with it. When the holder leaves, the partner gets
///     it. A following cuddlefish stays with its holder when it drops out of their loaded cells (the only server change,
///     <c>EntitySimulation.FollowsItsHolder</c>). Its follow/stay state is metadata, kept across a restart. The rest
///     documents the server's existing lock rules that the game side (CuteFishHandTarget patches, the L3 scenario
///     P1-CuddlefishBoth) builds on.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CuddlefishLockTest
{
    // A released cuddlefish, as Items.Dropped sends it: a cell entity at level Medium (seen in game)
    private static readonly NitroxVector3 position = new(48, -17, 226);

    [TestMethod]
    public async Task PlayingTakesTheLock_ItIsKeptAfterDroppingBack_AndGoesToThePartnerOnDisconnect()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-P1-CuddlefishLock");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity cuddlefish = Cuddlefish(new CuddlefishMetadata(true, false, null));
        AbsoluteEntityCell cell = cuddlefish.AbsoluteEntityCell;
        foreach (FakePlayer player in new[] { alice, bob })
        {
            player.Send(new CellVisibilityChanged(player.SessionId, [cell], []));
            await player.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));
        }

        // Alice releases it from the tank: her game simulates it
        alice.Send(new EntitySpawnedByClient(cuddlefish, true));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Exists(entity => entity.Id == cuddlefish.Id));
        AssertHolder(server, cuddlefish.Id, "Alice", SimulationLockType.TRANSIENT);

        // A normal request doesn't take it (no "nearest player" handover)
        (await RequestAsync(bob, cuddlefish.Id, SimulationLockType.TRANSIENT)).Should().BeFalse();
        AssertHolder(server, cuddlefish.Id, "Alice", SimulationLockType.TRANSIENT);

        // Bob plays with it
        alice.ClearReceived();
        (await RequestAsync(bob, cuddlefish.Id, SimulationLockType.EXCLUSIVE)).Should().BeTrue("playing with it takes it from Alice's normal lock");
        await alice.WaitForAsync<SimulationOwnershipChange>(packet => packet.Entities.Exists(entity => entity.Id == cuddlefish.Id && entity.SessionId == bob.SessionId));
        AssertHolder(server, cuddlefish.Id, "Bob", SimulationLockType.EXCLUSIVE);
        (await RequestAsync(alice, cuddlefish.Id, SimulationLockType.EXCLUSIVE)).Should().BeFalse("Alice can't take it while Bob is playing with it");

        // The play scene ends: Bob drops back to a normal lock and keeps it
        (await RequestAsync(bob, cuddlefish.Id, SimulationLockType.TRANSIENT)).Should().BeTrue();
        AssertHolder(server, cuddlefish.Id, "Bob", SimulationLockType.TRANSIENT);
        (await RequestAsync(alice, cuddlefish.Id, SimulationLockType.TRANSIENT)).Should().BeFalse("it follows Bob until Alice plays with it");
        AssertHolder(server, cuddlefish.Id, "Bob", SimulationLockType.TRANSIENT);

        // Bob leaves: Alice, who sees it, simulates it
        bob.Dispose();
        await alice.WaitForAsync<SimulationOwnershipChange>(packet => packet.Entities.Exists(entity => entity.Id == cuddlefish.Id && entity.SessionId == alice.SessionId));
        AssertHolder(server, cuddlefish.Id, "Alice", SimulationLockType.TRANSIENT);
    }

    [TestMethod]
    [DataRow(true, "Bob", DisplayName = "Following: Bob keeps it")]
    [DataRow(false, "Alice", DisplayName = "Staying: an ordinary creature of its cell")]
    public async Task HolderLosesSightOfIt_AFollowingCuddlefishStaysTheirs(bool following, string expectedHolder)
    {
        // Seen in the L3 scenario: Bob played with it and sped off; its cell left his loaded cells before his game warped it to
        // him, and the server gave it to Alice, who could still see it. It then followed Alice.
        await using ServerHarness server = await ServerHarness.StartAsync("L2-P1-CuddlefishOutOfSight");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity cuddlefish = Cuddlefish(new CuddlefishMetadata(following, false, null));
        AbsoluteEntityCell cell = cuddlefish.AbsoluteEntityCell;
        foreach (FakePlayer player in new[] { alice, bob })
        {
            player.Send(new CellVisibilityChanged(player.SessionId, [cell], []));
            await player.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));
        }
        alice.Send(new EntitySpawnedByClient(cuddlefish, true));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Exists(entity => entity.Id == cuddlefish.Id));
        (await RequestAsync(bob, cuddlefish.Id, SimulationLockType.EXCLUSIVE)).Should().BeTrue();
        (await RequestAsync(bob, cuddlefish.Id, SimulationLockType.TRANSIENT)).Should().BeTrue();

        bob.ClearReceived();
        bob.Send(new CellVisibilityChanged(bob.SessionId, [], [cell]));
        // The reply to a cell change always comes; a later one proves the first was processed
        AbsoluteEntityCell otherCell = new(new NitroxVector3(500, -10, -400), 1);
        bob.Send(new CellVisibilityChanged(bob.SessionId, [otherCell], []));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(otherCell));

        AssertHolder(server, cuddlefish.Id, expectedHolder, SimulationLockType.TRANSIENT);
    }

    [TestMethod]
    public async Task StayCommand_ReachesThePartner_AndSurvivesARestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-P1-CuddlefishStay");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity cuddlefish = Cuddlefish(new CuddlefishMetadata(true, false, new WaterParkCreatureMetadata(1, 2376.07, -1, true)));
        AbsoluteEntityCell cell = cuddlefish.AbsoluteEntityCell;
        foreach (FakePlayer player in new[] { alice, bob })
        {
            player.Send(new CellVisibilityChanged(player.SessionId, [cell], []));
            await player.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));
        }
        alice.Send(new EntitySpawnedByClient(cuddlefish, true));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Exists(entity => entity.Id == cuddlefish.Id));

        // Bob tells it to stay (CuteFishHandTarget_PlayCommandAnimation_Patch)
        CuddlefishMetadata stay = new(false, false, new WaterParkCreatureMetadata(1, 2376.07, -1, true));
        bob.Send(new EntityMetadataUpdate(cuddlefish.Id, stay));
        EntityMetadataUpdate update = await alice.WaitForAsync<EntityMetadataUpdate>(packet => packet.Id == cuddlefish.Id);
        update.NewValue.Should().BeOfType<CuddlefishMetadata>().Which.FollowingPlayer.Should().BeFalse();

        await server.RestartAsync();
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        aliceAgain.Send(new CellVisibilityChanged(aliceAgain.SessionId, [cell], []));
        SpawnEntities spawn = await aliceAgain.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));
        Entity loaded = spawn.Entities.Find(entity => entity.Id == cuddlefish.Id);
        loaded.Should().NotBeNull("the cuddlefish is still where it stays");
        CuddlefishMetadata metadata = loaded!.Metadata.Should().BeOfType<CuddlefishMetadata>().Subject;
        metadata.FollowingPlayer.Should().BeFalse("it still stays after the restart");
        metadata.WaterParkCreature.Age.Should().Be(1, "it keeps its age (size)");
    }

    private static WorldEntity Cuddlefish(CuddlefishMetadata metadata)
    {
        NitroxTransform transform = new(position, NitroxQuaternion.Identity, NitroxVector3.One);
        return new WorldEntity(transform, 1, "d4be3a5d-67c3-4345-af25-7663da2d2898", false, new NitroxId(), new NitroxTechType("Cutefish"), metadata, null, []);
    }

    private static async Task<bool> RequestAsync(FakePlayer player, NitroxId id, SimulationLockType lockType)
    {
        player.ClearReceived();
        player.Send(new SimulationOwnershipRequest(player.SessionId, id, lockType));
        SimulationOwnershipResponse response = await player.WaitForAsync<SimulationOwnershipResponse>(packet => packet.Id == id && packet.LockType == lockType);
        return response.LockAcquired;
    }

    private static void AssertHolder(ServerHarness server, NitroxId id, string name, SimulationLockType lockType)
    {
        server.Get<SimulationOwnershipData>().TryGetLock(id, out SimulationOwnershipData.PlayerLock playerLock).Should().BeTrue();
        playerLock.Player.Name.Should().Be(name);
        playerLock.LockType.Should().Be(lockType);
    }
}
