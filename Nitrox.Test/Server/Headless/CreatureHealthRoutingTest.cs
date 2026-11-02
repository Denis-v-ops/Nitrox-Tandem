using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Processors;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     MULTINAUGHTICA_SPEC M5 (upstream #2903 + MultiNaughtica's hand-over grace), the server half: only the game that
///     simulates a creature changes its health. A hit by another player is sent to that game (<see cref="CreatureDamageAction" />),
///     or back to the sender when nobody simulates the creature, and the simulating game shares the health
///     (<see cref="CreatureHealthChanged" />) with the players who see the creature. What the games do with it is checked in
///     game by M5-CreatureHealth.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CreatureHealthRoutingTest
{
    // One level-0 cell (16 m cubes from a multiple of 16) for the creatures, one far away
    private static readonly NitroxVector3 position = new(-152, -8, 296);
    private static readonly AbsoluteEntityCell cell = new(position, 0);
    private static readonly AbsoluteEntityCell farCell = new(new NitroxVector3(900, -10, -900), 0);

    [TestMethod]
    public async Task HitByThePlayerWhoDoesNotSimulate_ReachesOnlyTheSimulatingPlayer()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M5-Hit");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        WorldEntity stalker = await SpawnAsync(alice, position, "Stalker");

        bob.Send(Hit(stalker.Id, 25f));
        CreatureDamageAction hit = await alice.WaitForAsync<CreatureDamageAction>();
        hit.CreatureId.Should().Be(stalker.Id);
        hit.OriginalDamage.Should().Be(25f);
        hit.Type.Should().Be(DamageType.Normal);

        // A second hit, from Carol: once Alice has it, the server has handled Bob's packet for everyone
        carol.Send(Hit(stalker.Id, 26f));
        (await alice.WaitForAsync<CreatureDamageAction>(packet => packet.OriginalDamage == 26f)).CreatureId.Should().Be(stalker.Id);
        await Task.Delay(200);

        alice.ReceivedOf<CreatureDamageAction>().Should().HaveCount(2, "Alice simulates the stalker and gets both hits");
        bob.ReceivedOf<CreatureDamageAction>().Should().BeEmpty("only the simulating player gets a hit");
        carol.ReceivedOf<CreatureDamageAction>().Should().BeEmpty("only the simulating player gets a hit");
    }

    [TestMethod]
    public async Task HitOnACreatureNobodySimulates_GoesBackToTheSender_AndASecondHopIsDropped()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M5-NoHolder");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        // Not on the server (a creature nobody simulates has no lock either): the sender applies its own hit
        NitroxId unknownCreature = new();

        bob.Send(Hit(unknownCreature, 25f));
        (await bob.WaitForAsync<CreatureDamageAction>()).CreatureId.Should().Be(unknownCreature);

        // A hit that a game re-sent because it didn't have the creature (Hops 1) has nobody left to reach: dropped. Then a
        // first hit again: once it is back, the re-sent one was handled.
        bob.ClearReceived();
        bob.Send(new CreatureDamageAction(unknownCreature, 30f, DamageType.Normal, NitroxVector3.Zero, Optional.Empty, 1));
        bob.Send(Hit(unknownCreature, 31f));
        await bob.WaitForAsync<CreatureDamageAction>(packet => packet.OriginalDamage == 31f);
        await Task.Delay(200);

        bob.ReceivedOf<CreatureDamageAction>().Should().ContainSingle("the re-sent hit is dropped");
        alice.ReceivedOf<CreatureDamageAction>().Should().BeEmpty("nobody else gets a hit on a creature nobody simulates");
    }

    [TestMethod]
    public async Task HealthReport_ReachesThePlayersWhoSeeTheCreature_OnlyFromItsSimulatingPlayer()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M5-Report");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        WorldEntity stalker = await SpawnAsync(alice, position, "Stalker");
        await SeeAsync(bob, cell);
        await SeeAsync(carol, farCell);

        // Bob doesn't simulate the stalker: his report is dropped. Then Alice's: once Bob has it, Bob's was handled.
        bob.Send(new CreatureHealthChanged(stalker.Id, 50f));
        alice.Send(new CreatureHealthChanged(stalker.Id, 240f));
        (await bob.WaitForAsync<CreatureHealthChanged>()).Health.Should().Be(240f);
        await Task.Delay(200);

        bob.ReceivedOf<CreatureHealthChanged>().Should().ContainSingle("the report isn't sent back to its sender's game either");
        alice.ReceivedOf<CreatureHealthChanged>().Should().BeEmpty("Bob doesn't simulate the stalker");
        carol.ReceivedOf<CreatureHealthChanged>().Should().BeEmpty("Carol doesn't see the stalker");
    }

    /// <summary>
    ///     MultiNaughtica's hand-over grace: the previous simulating player's last report can come after the lock moved
    ///     (its game throttles reports to one every 0.2 s), and it carries hits the new simulating player doesn't know of.
    ///     It is relayed within <see cref="CreatureHealthChangedProcessor.PREVIOUS_OWNER_GRACE_MS" />, until the new one
    ///     reports.
    /// </summary>
    [TestMethod]
    public async Task LastReportOfThePreviousSimulatingPlayer_IsRelayedRightAfterTheHandOver_UntilTheNewOneReports()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M5-HandOver");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity stalker = await SpawnAsync(alice, position, "Stalker");
        await SeeAsync(alice, cell);
        await SeeAsync(bob, cell);

        // Alice swims away: the server hands the stalker to Bob, who still sees it. Her throttled report comes after that.
        alice.Send(new CellVisibilityChanged(alice.SessionId, [], [cell]));
        await bob.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(entity => entity.Id == stalker.Id && entity.SessionId == bob.SessionId));
        server.Get<SimulationOwnershipData>().GetPlayerForLock(stalker.Id)!.SessionId.Should().Be(bob.SessionId);
        alice.Send(new CreatureHealthChanged(stalker.Id, 200f));
        (await bob.WaitForAsync<CreatureHealthChanged>()).Health.Should().Be(200f, "Alice's last report is still relayed after the hand-over");

        // Bob's own report makes him the up-to-date one: a later report of Alice's is dropped. Alice sees the cell again to
        // get Bob's report (the barrier: once she has it, the server handled her report before it).
        await SeeAsync(alice, cell);
        bob.Send(new CreatureHealthChanged(stalker.Id, 180f));
        (await alice.WaitForAsync<CreatureHealthChanged>()).Health.Should().Be(180f);
        alice.Send(new CreatureHealthChanged(stalker.Id, 150f));
        bob.Send(new CreatureHealthChanged(stalker.Id, 170f));
        await alice.WaitForAsync<CreatureHealthChanged>(packet => packet.Health == 170f);
        await Task.Delay(200);

        bob.ReceivedOf<CreatureHealthChanged>().Should().ContainSingle("Alice's report after Bob's is out of date");
    }

    [TestMethod]
    public async Task ReportOfThePreviousSimulatingPlayer_IsDroppedAfterTheGraceWindow()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M5-GraceEnds");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity stalker = await SpawnAsync(alice, position, "Stalker");
        await SeeAsync(alice, cell);
        await SeeAsync(bob, cell);

        alice.Send(new CellVisibilityChanged(alice.SessionId, [], [cell]));
        await bob.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(entity => entity.Id == stalker.Id && entity.SessionId == bob.SessionId));
        await Task.Delay(TimeSpan.FromMilliseconds(CreatureHealthChangedProcessor.PREVIOUS_OWNER_GRACE_MS + 500));

        // Too late for Alice's report. Bob's own report after it is the barrier.
        alice.Send(new CreatureHealthChanged(stalker.Id, 200f));
        await Task.Delay(200);
        await SeeAsync(alice, cell);
        bob.Send(new CreatureHealthChanged(stalker.Id, 180f));
        await alice.WaitForAsync<CreatureHealthChanged>(packet => packet.Health == 180f);
        await Task.Delay(200);

        bob.ReceivedOf<CreatureHealthChanged>().Should().BeEmpty("the grace window had passed");
    }

    private static CreatureDamageAction Hit(NitroxId creatureId, float damage)
    {
        return new CreatureDamageAction(creatureId, damage, DamageType.Normal, NitroxVector3.Zero, Optional.Empty, 0);
    }

    private static async Task SeeAsync(FakePlayer player, AbsoluteEntityCell visibleCell)
    {
        player.ClearReceived();
        player.Send(new CellVisibilityChanged(player.SessionId, [visibleCell], []));
        await player.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(visibleCell));
    }

    /// <summary>
    ///     What a game sends for something it spawns (console "spawn"): the spawner gets its lock.
    /// </summary>
    private static async Task<WorldEntity> SpawnAsync(FakePlayer spawner, NitroxVector3 spawnPosition, string techType)
    {
        WorldEntity entity = new(new NitroxTransform(spawnPosition, NitroxQuaternion.Identity, NitroxVector3.One), 0, $"{techType}-class-id", false, new NitroxId(),
                                 new NitroxTechType(techType), null, null, []);
        spawner.Send(new EntitySpawnedByClient(entity, false, true));
        await spawner.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(simulated => simulated.Id == entity.Id && simulated.SessionId == spawner.SessionId));
        return entity;
    }
}
