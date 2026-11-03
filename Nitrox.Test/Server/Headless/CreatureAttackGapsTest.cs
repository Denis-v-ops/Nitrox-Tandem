using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     MULTINAUGHTICA_SPEC M4, the server half: crash fish keep their place through a restart, and the Reaper grab and the
///     Crabsquid EMP reach the players who see the creature. (What the games do with it is checked in game by
///     M4-CreatureAttacks. World bleeders: COOP_FEATURES_SPEC B1 and FIX_SPEC S15d, WarperSpawnerTest.)
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CreatureAttackGapsTest
{
    /// <summary>
    ///     The game that makes a crash fish sends it with its world position and its home as parent (CrashHome_Spawn_Patch).
    ///     The save loader hung every child's transform under its parent's without converting, which reads that world
    ///     position as a local one: after a restart the fish was in another cell on the server and far from its home in both
    ///     games (the transform's parent goes over the wire too).
    /// </summary>
    [TestMethod]
    public async Task CrashFish_StaysAtItsHomeThroughARestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M4-CrashFish");
        FakePlayer alice = await server.JoinAsync("Alice");
        // In the middle of a level-0 cell (16 m cubes from a multiple of 16)
        NitroxVector3 homePosition = new(-56, -24, 120);
        NitroxVector3 fishPosition = homePosition + new NitroxVector3(0, 0.4f, 0);
        AbsoluteEntityCell cell = new(fishPosition, 0);
        WorldEntity home = new(new NitroxTransform(homePosition, NitroxQuaternion.FromEuler(-90, 35, 0), NitroxVector3.One), 0, "crash-home-class-id", false, new NitroxId(),
                               new NitroxTechType("CrashHome"), new CrashHomeMetadata(-1), null, []);
        WorldEntity fish = new(new NitroxTransform(fishPosition, NitroxQuaternion.FromEuler(-90, 35, 0), NitroxVector3.One), 0, "crash-class-id", false, new NitroxId(),
                               new NitroxTechType("Crash"), null, home.Id, []);
        alice.Send(new EntitySpawnedByClient(home, false, false));
        alice.Send(new EntitySpawnedByClient(fish));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.TryGetEntityById(fish.Id, out WorldEntity _), "the server knows the fish");
        server.IsInCell(fish.Id, cell).Should().BeTrue("during the session the fish is where its game put it");

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(fish.Id, out WorldEntity reloaded).Should().BeTrue();
        reloaded.ParentId.Should().Be(home.Id);
        AssertNear(reloaded.Transform.Position, fishPosition, "the server's fish after the restart");
        server.IsInCell(fish.Id, cell).Should().BeTrue("the fish is in its home's cell after the restart");

        FakePlayer bob = await server.JoinAsync("Bob");
        bob.Send(new CellVisibilityChanged(bob.SessionId, [cell], []));
        SpawnEntities reply = await bob.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));
        List<WorldEntity> fishSent = reply.Entities.SelectMany(Flatten).OfType<WorldEntity>().Where(entity => entity.Id == fish.Id).ToList();
        fishSent.Should().NotBeEmpty("the home's cell holds the fish");
        foreach (WorldEntity sent in fishSent)
        {
            AssertNear(sent.Transform.Position, fishPosition, "the fish Bob's game spawns (CrashEntitySpawner places it at Transform.Position)");
        }
    }

    /// <summary>
    ///     The Crabsquid's EMP blast and the Reaper's grab and release are replayed by the games that see the creature; a
    ///     player far away doesn't get them (without a server processor they would be relayed to everyone).
    /// </summary>
    [TestMethod]
    public async Task EmpBlastAndReaperGrab_ReachThePlayersWhoSeeTheCreature()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M4-Relays");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        // All three in one level-0 cell (16 m cubes from a multiple of 16)
        NitroxVector3 position = new(-152, -8, 296);
        AbsoluteEntityCell cell = new(position, 0);
        WorldEntity crabsquid = await SpawnAsync(alice, position, "CrabSquid");
        WorldEntity reaper = await SpawnAsync(alice, position + new NitroxVector3(3, 0, 0), "ReaperLeviathan");
        WorldEntity seamoth = await SpawnAsync(alice, position + new NitroxVector3(-3, 0, 0), "Seamoth");
        bob.Send(new CellVisibilityChanged(bob.SessionId, [cell], []));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));

        alice.Send(new EmpFired(crabsquid.Id));
        alice.Send(new ReaperGrabVehicle(reaper.Id, seamoth.Id));
        alice.Send(new ReaperReleaseVehicle(reaper.Id));

        (await bob.WaitForAsync<EmpFired>()).CreatureId.Should().Be(crabsquid.Id);
        ReaperGrabVehicle grab = await bob.WaitForAsync<ReaperGrabVehicle>();
        grab.ReaperId.Should().Be(reaper.Id);
        grab.VehicleId.Should().Be(seamoth.Id);
        (await bob.WaitForAsync<ReaperReleaseVehicle>()).ReaperId.Should().Be(reaper.Id);

        // Carol's own cell load is answered after the server handled Alice's packets above
        AbsoluteEntityCell farCell = new(new NitroxVector3(900, -10, -900), 0);
        carol.Send(new CellVisibilityChanged(carol.SessionId, [farCell], []));
        await carol.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(farCell));
        await carol.AssertNotReceivedAsync<EmpFired>(quietPeriod: TimeSpan.FromMilliseconds(200));
        carol.ReceivedOf<ReaperGrabVehicle>().Should().BeEmpty("Carol doesn't see the Reaper");
        carol.ReceivedOf<ReaperReleaseVehicle>().Should().BeEmpty("Carol doesn't see the Reaper");
        alice.ReceivedOf<EmpFired>().Should().BeEmpty("the blast isn't sent back to the game that cast it");
    }

    /// <summary>
    ///     What a game sends for something it spawns (console "spawn"): the spawner gets its lock.
    /// </summary>
    private static async Task<WorldEntity> SpawnAsync(FakePlayer spawner, NitroxVector3 position, string techType)
    {
        WorldEntity entity = new(new NitroxTransform(position, NitroxQuaternion.Identity, NitroxVector3.One), 0, $"{techType}-class-id", false, new NitroxId(),
                                 new NitroxTechType(techType), null, null, []);
        spawner.Send(new EntitySpawnedByClient(entity, false, true));
        await spawner.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(simulated => simulated.Id == entity.Id && simulated.SessionId == spawner.SessionId));
        return entity;
    }

    private static IEnumerable<Entity> Flatten(Entity entity) => entity.ChildEntities.SelectMany(Flatten).Prepend(entity);

    private static void AssertNear(NitroxVector3 actual, NitroxVector3 expected, string what)
    {
        NitroxVector3.Distance(actual, expected).Should().BeLessThan(0.01f, $"{what} should be at {expected}, is at {actual}");
    }
}
