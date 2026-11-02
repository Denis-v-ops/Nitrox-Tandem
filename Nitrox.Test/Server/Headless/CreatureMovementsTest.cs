using System.Threading.Tasks;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;
using static Nitrox.Test.Server.Headless.CreatureLeavesSimulatorTest;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     FIX_SPEC S16b/c, the server half: the simulating game streams a creature's pose (<see cref="CreatureMovements" />).
///     The partner who sees it gets each pose with the sender's time (for dead reckoning), the server's record follows, and a
///     player who arrives later spawns the creature at the latest pose instead of where its AI last picked a swim target
///     (up to 34 m off before).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CreatureMovementsTest
{
    private static readonly NitroxVector3 start = new(300, -80, 600);
    private static readonly NitroxVector3 farAway = new(1300, -80, 600);

    [TestMethod]
    public async Task PosesReachThePartnerWhoSeesIt_AndALateArrivalSpawnsItAtTheLatestPose()
    {
        AbsoluteEntityCell cell = new(start, LEVEL);
        AbsoluteEntityCell farCell = new(farAway, LEVEL);

        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureMovements");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        await SeeCellsAsync(alice, cell);
        await SeeCellsAsync(bob, cell);
        await SeeCellsAsync(carol, farCell);
        WorldEntity reaper = await SpawnCreatureAsync(alice, start);

        // Alice streams it swimming 1 m per pose along x.
        for (int i = 1; i <= 5; i++)
        {
            alice.Send(Pose(reaper, start + new NitroxVector3(i, 0, 0), 100 + i * 0.1));
        }
        CreatureMovements last = await bob.WaitForAsync<CreatureMovements>(p => p.Data.Exists(m => m.Id == reaper.Id && m.Position.X >= start.X + 5));
        last.RealTime.Should().BeApproximately(100.5, 1e-9);
        last.Data.Should().ContainSingle().Which.Velocity.X.Should().Be(10);
        Position(server, reaper).X.Should().BeApproximately(start.X + 5, 0.01f);

        // Carol, 1 km away, gets none of them.
        await SyncAsync(carol);
        carol.ReceivedOf<CreatureMovements>().Should().BeEmpty();

        // S16c: Carol arrives: her game spawns it at the latest pose.
        carol.Send(new CellVisibilityChanged(carol.SessionId, [cell], []));
        SpawnEntities spawn = await carol.WaitForAsync<SpawnEntities>(p => p.SpawnedCells.Contains(cell));
        WorldEntity spawned = spawn.Entities.OfType<WorldEntity>().Single(e => e.Id == reaper.Id);
        spawned.Transform.Position.X.Should().BeApproximately(start.X + 5, 0.01f);
    }

    [TestMethod]
    public async Task PosesFromAPlayerWithoutTheLock_AreIgnored()
    {
        AbsoluteEntityCell cell = new(start, LEVEL);

        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureMovementsNoLock");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await SeeCellsAsync(alice, cell);
        await SeeCellsAsync(bob, cell);
        WorldEntity reaper = await SpawnCreatureAsync(alice, start);

        bob.Send(Pose(reaper, start + new NitroxVector3(20, 0, 0), 100));
        await SyncAsync(bob);
        Position(server, reaper).X.Should().BeApproximately(start.X, 0.01f);
        await SyncAsync(alice);
        alice.ReceivedOf<CreatureMovements>().Should().BeEmpty();
    }

    private static CreatureMovements Pose(WorldEntity creature, NitroxVector3 position, double realTime)
    {
        return new CreatureMovements([new CreatureMovement(creature.Id, position, NitroxQuaternion.Identity, new NitroxVector3(10, 0, 0), NitroxVector3.Zero)], realTime);
    }
}
