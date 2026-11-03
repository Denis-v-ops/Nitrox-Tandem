using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;
using static Nitrox.Test.Server.Headless.CreatureLeavesSimulatorTest;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC B1, the server half. A warper spawner is a world entity the server spawns from the game's batch
///     files; it must have a simulating player, because only that player's game spawns its warper (WarperSpawner_Update_Patch).
///     The warper it registers reaches the partner once, and its warp-out (EntityDestroyed) removes it for everyone. Bleeders,
///     also spawned by the server, need a simulating player to have any AI. (What the games do is checked in game by
///     B1-WarpersAndBleeders.)
/// </summary>
[TestClass]
[TestCategory("L2")]
public class WarperSpawnerTest
{
    private const int LEVEL = 1;
    private static readonly NitroxVector3 spawnerPosition = new(-700, -560, 1200);

    [TestMethod]
    public async Task ServerSpawnedWarperSpawnerAndBleeder_AreSimulatedByTheFirstPlayerWhoSeesThem()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-B1-Spawner");
        WorldEntity spawner = AddServerSpawned(server, "WarperSpawner", spawnerPosition);
        WorldEntity bleeder = AddServerSpawned(server, "Bleeder", spawnerPosition + new NitroxVector3(2, 0, 0));
        AbsoluteEntityCell cell = new(spawnerPosition, LEVEL);
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        await SeeCellsAsync(alice, cell);
        await alice.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(e => e.Id == spawner.Id && e.SessionId == alice.SessionId));
        await alice.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(e => e.Id == bleeder.Id && e.SessionId == alice.SessionId));
        LockOwner(server, spawner).Should().Be(alice.SessionId);
        LockOwner(server, bleeder).Should().Be(alice.SessionId);

        // The partner arriving doesn't take them over: one game spawns the warpers.
        await SeeCellsAsync(bob, cell);
        LockOwner(server, spawner).Should().Be(alice.SessionId);
        bob.ReceivedOf<SimulationOwnershipChange>().Should().NotContain(change => change.Entities.Any(e => e.Id == spawner.Id && e.SessionId == bob.SessionId));
    }

    [TestMethod]
    public async Task WarperOfTheSimulatingGame_ReachesThePartnerOnce_AndItsWarpOutRemovesIt()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-B1-Warper");
        WorldEntity spawner = AddServerSpawned(server, "WarperSpawner", spawnerPosition);
        AbsoluteEntityCell cell = new(spawnerPosition, LEVEL);
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await SeeCellsAsync(alice, cell);
        await SeeCellsAsync(bob, cell);
        LockOwner(server, spawner).Should().Be(alice.SessionId);

        // What Items.Dropped sends for the warper Alice's spawner just instantiated
        WorldEntity warper = new(new NitroxTransform(spawnerPosition + new NitroxVector3(0, 2, 0), NitroxQuaternion.Identity, NitroxVector3.One), LEVEL,
                                 "c7103527-f6fa-4d1e-a75d-146433851557", false, new NitroxId(), new NitroxTechType("Warper"), null, null, []);
        alice.Send(new EntitySpawnedByClient(warper, true));

        await alice.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(e => e.Id == warper.Id && e.SessionId == alice.SessionId));
        await bob.WaitForAsync<SpawnEntities>(spawn => spawn.Entities.Any(e => e.Id == warper.Id));
        LockOwner(server, warper).Should().Be(alice.SessionId);
        await SyncAsync(alice);
        bob.ReceivedOf<SpawnEntities>().Count(spawn => spawn.Entities.Any(e => e.Id == warper.Id)).Should().Be(1);
        alice.ReceivedOf<SpawnEntities>().Should().NotContain(spawn => spawn.Entities.Any(e => e.Id == warper.Id), "the spawning game has it already");

        alice.Send(new EntityDestroyed(warper.Id));
        await bob.WaitForAsync<EntityDestroyed>(packet => packet.Id == warper.Id);
        server.EntityRegistry.TryGetEntityById(warper.Id, out Entity _).Should().BeFalse();
        server.EntityRegistry.TryGetEntityById(spawner.Id, out Entity _).Should().BeTrue("the spawner stays and spawns the next warper");
    }

    private static WorldEntity AddServerSpawned(ServerHarness server, string techType, NitroxVector3 position)
    {
        WorldEntity entity = new(new NitroxTransform(position, NitroxQuaternion.Identity, NitroxVector3.One), LEVEL, $"{techType}-class-id", true, new NitroxId(),
                                 new NitroxTechType(techType), null, null, []);
        server.EntityRegistry.AddOrUpdate(entity);
        server.WorldEntityManager.TrackEntityInTheWorld(entity);
        return entity;
    }
}
