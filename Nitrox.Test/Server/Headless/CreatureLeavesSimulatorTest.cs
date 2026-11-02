using System.Threading.Tasks;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     FIX_SPEC S16, the server half: a creature that swims out of its simulating player's loaded cells (a Reaper patrolling
///     away from one player towards the other) must be simulated by a player who still has it loaded. Before, the lock
///     moved only when the simulating player's *own* cells unloaded, so the creature kept its lock holder, whose game had
///     already put it to sleep: it froze for the partner, who could see it but not run its AI.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CreatureLeavesSimulatorTest
{
    // A Reaper is LargeWorldEntity.CellLevel.VeryFar (level 3, read in game: S16-LeviathanSync), 32 m cells.
    private const int LEVEL = 3;
    private static readonly NitroxVector3 start = new(300, -80, 600);
    private static readonly NitroxVector3 nearby = new(310, -80, 600);
    private static readonly NitroxVector3 away = new(380, -80, 600);

    [TestMethod]
    public async Task CreatureSwimsIntoOnlyThePartnersCells_PartnerTakesItOver()
    {
        AbsoluteEntityCell startCell = new(start, LEVEL);
        AbsoluteEntityCell awayCell = new(away, LEVEL);
        startCell.Should().NotBe(awayCell);
        new AbsoluteEntityCell(nearby, LEVEL).Should().Be(startCell);

        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureLeaves");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await SeeCellsAsync(alice, startCell);
        await SeeCellsAsync(bob, startCell, awayCell);
        WorldEntity reaper = await SpawnCreatureAsync(alice, start);

        // Moving inside Alice's cells changes nothing.
        alice.Send(Move(reaper, nearby));
        await SyncAsync(alice);
        LockOwner(server, reaper).Should().Be(alice.SessionId);

        // Into a cell only Bob has loaded: Bob simulates it from now on, and Alice is told.
        alice.Send(Move(reaper, away));
        await bob.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(e => e.Id == reaper.Id && e.SessionId == bob.SessionId));
        await alice.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(e => e.Id == reaper.Id && e.SessionId == bob.SessionId));
        LockOwner(server, reaper).Should().Be(bob.SessionId);

        // Alice's late updates no longer count; Bob's do.
        alice.Send(Move(reaper, start));
        bob.Send(Move(reaper, away + new NitroxVector3(5, 0, 0)));
        await ServerHarness.WaitUntilAsync(() => Position(server, reaper).X > away.X + 4, "Bob's update to be applied");
        Position(server, reaper).X.Should().BeApproximately(away.X + 5, 0.01f);
    }

    /// <summary>
    ///     What the S16 scenario saw in game: the simulating game keeps a creature that swam out of its loaded batches as an
    ///     <c>OutOfCellEntity</c> (PlayerSeeOutOfCellEntity), so the server still counted it as seen and kept the lock there,
    ///     780 m from the creature, while the partner had its cell loaded.
    /// </summary>
    [TestMethod]
    public async Task CreatureKeptOutOfCellBySimulator_GoesToThePartnerWhoHasItsCell()
    {
        AbsoluteEntityCell startCell = new(start, LEVEL);
        AbsoluteEntityCell awayCell = new(away, LEVEL);

        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureOutOfCell");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await SeeCellsAsync(alice, startCell);
        await SeeCellsAsync(bob, awayCell);
        WorldEntity reaper = await SpawnCreatureAsync(alice, start);

        alice.Send(new PlayerSeeOutOfCellEntity(reaper.Id));
        alice.Send(Move(reaper, away));
        await bob.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(e => e.Id == reaper.Id && e.SessionId == bob.SessionId));
        LockOwner(server, reaper).Should().Be(bob.SessionId);
    }

    [TestMethod]
    public async Task CreatureSwimsWhereNobodyIs_KeepsItsSimulator_UntilSomeoneArrives()
    {
        AbsoluteEntityCell startCell = new(start, LEVEL);
        AbsoluteEntityCell awayCell = new(away, LEVEL);

        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureLeavesNobody");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await SeeCellsAsync(alice, startCell);
        WorldEntity reaper = await SpawnCreatureAsync(alice, start);

        // Nobody else sees it: Alice keeps it (her game may still run it, e.g. when that batch isn't loaded there).
        alice.Send(Move(reaper, away));
        await SyncAsync(alice);
        LockOwner(server, reaper).Should().Be(alice.SessionId);

        // Bob arrives: he spawns it and simulates it, although Alice still holds a lock on it that she can't use.
        bob.Send(new CellVisibilityChanged(bob.SessionId, [awayCell], []));
        SpawnEntities spawn = await bob.WaitForAsync<SpawnEntities>(p => p.SpawnedCells.Contains(awayCell));
        spawn.Entities.Should().Contain(e => e.Id == reaper.Id);
        await bob.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(e => e.Id == reaper.Id && e.SessionId == bob.SessionId));
        LockOwner(server, reaper).Should().Be(bob.SessionId);
    }

    [TestMethod]
    public async Task PartnerArrivesWhileTheSimulatorStillSeesIt_SimulatorKeepsIt()
    {
        AbsoluteEntityCell startCell = new(start, LEVEL);

        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureStays");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await SeeCellsAsync(alice, startCell);
        WorldEntity reaper = await SpawnCreatureAsync(alice, start);

        await SeeCellsAsync(bob, startCell);
        LockOwner(server, reaper).Should().Be(alice.SessionId);
        bob.ReceivedOf<SimulationOwnershipChange>().Should().NotContain(change => change.Entities.Any(e => e.Id == reaper.Id && e.SessionId == bob.SessionId));
    }

    private static async Task SeeCellsAsync(FakePlayer player, params AbsoluteEntityCell[] cells)
    {
        player.Send(new CellVisibilityChanged(player.SessionId, [.. cells], []));
        foreach (AbsoluteEntityCell cell in cells)
        {
            await player.WaitForAsync<SpawnEntities>(p => p.SpawnedCells.Contains(cell));
        }
    }

    /// <summary>
    ///     Waits until the server has handled this player's earlier packets: it answers every cell visibility change, even
    ///     an empty one, and handles one player's packets in order.
    /// </summary>
    private static async Task SyncAsync(FakePlayer player)
    {
        int before = player.ReceivedOf<SpawnEntities>().Count;
        player.Send(new CellVisibilityChanged(player.SessionId, [], []));
        await ServerHarness.WaitUntilAsync(() => player.ReceivedOf<SpawnEntities>().Count > before, $"{player.Name}'s earlier packets to be handled");
    }

    private static EntityTransformUpdates Move(WorldEntity creature, NitroxVector3 position)
    {
        return new EntityTransformUpdates([
            new EntityTransformUpdates.SplineTransformUpdate(creature.Id, position, NitroxQuaternion.Identity, position + new NitroxVector3(20, 0, 0), new NitroxVector3(1, 0, 0), 10)
        ]);
    }

    private static SessionId? LockOwner(ServerHarness server, WorldEntity entity)
    {
        return server.Get<SimulationOwnershipData>().GetPlayerForLock(entity.Id)?.SessionId;
    }

    private static NitroxVector3 Position(ServerHarness server, WorldEntity entity)
    {
        server.EntityRegistry.TryGetEntityById(entity.Id, out WorldEntity registered).Should().BeTrue();
        return registered.Transform.Position;
    }

    /// <summary>
    ///     What a game sends for a creature it spawns (console "spawn"): the spawner gets the creature's lock.
    /// </summary>
    private static async Task<WorldEntity> SpawnCreatureAsync(FakePlayer spawner, NitroxVector3 position)
    {
        WorldEntity creature = new(new NitroxTransform(position, NitroxQuaternion.Identity, NitroxVector3.One), LEVEL, "creature-class-id", false, new NitroxId(),
                                   new NitroxTechType("ReaperLeviathan"), null, null, []);
        spawner.Send(new EntitySpawnedByClient(creature, false, true));
        await spawner.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(entity => entity.Id == creature.Id && entity.SessionId == spawner.SessionId));
        return creature;
    }
}
