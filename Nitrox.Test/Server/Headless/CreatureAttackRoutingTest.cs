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
///     FIX_SPEC S14 (#2781), the server half: a creature's bite on another player is decided by the game that simulates
///     the creature and sent as <see cref="CreatureAttackedPlayer" />. The server passes it only to the bitten player,
///     and only from the creature's simulating player: a second game sending the same bite would hurt the player twice.
///     (What the games do with it is checked in game by S14-CreatureAggro.)
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CreatureAttackRoutingTest
{
    [TestMethod]
    public async Task AttackFromTheSimulator_ReachesOnlyTheBittenPlayer_OthersAreDropped()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureAttack");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        WorldEntity aliceStalker = await SpawnCreatureAsync(alice, new NitroxVector3(-80, -6, 440));
        WorldEntity bobBiter = await SpawnCreatureAsync(bob, new NitroxVector3(-80, -6, 460));

        alice.Send(new CreatureAttackedPlayer(aliceStalker.Id, bob.SessionId, 20, 0));
        CreatureAttackedPlayer bite = await bob.WaitForAsync<CreatureAttackedPlayer>();
        bite.CreatureId.Should().Be(aliceStalker.Id);
        bite.TargetSessionId.Should().Be(bob.SessionId);
        bite.Damage.Should().Be(20);

        // Bob doesn't simulate Alice's stalker, and nobody can bite themselves through the server. Then a valid bite of
        // Bob's biter: once Alice has it, the server has handled Bob's earlier packets (one sender's packets are handled in order).
        bob.Send(new CreatureAttackedPlayer(aliceStalker.Id, alice.SessionId, 20, 0));
        bob.Send(new CreatureAttackedPlayer(bobBiter.Id, bob.SessionId, 7, 0));
        bob.Send(new CreatureAttackedPlayer(bobBiter.Id, alice.SessionId, 7, 0));
        CreatureAttackedPlayer biterBite = await alice.WaitForAsync<CreatureAttackedPlayer>();
        biterBite.CreatureId.Should().Be(bobBiter.Id);
        await Task.Delay(200);

        alice.ReceivedOf<CreatureAttackedPlayer>().Should().ContainSingle("only the biter's bite was sent by its simulator");
        bob.ReceivedOf<CreatureAttackedPlayer>().Should().ContainSingle("the stalker's bite; Bob's bite on himself must not come back");
        carol.ReceivedOf<CreatureAttackedPlayer>().Should().BeEmpty("only the bitten player gets a bite");
    }

    [TestMethod]
    public async Task AttackAfterTheLockMoved_IsDropped()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureAttackLock");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity stalker = await SpawnCreatureAsync(alice, new NitroxVector3(-80, -6, 440));

        // Bob takes the stalker over (an exclusive lock breaks Alice's transient one).
        bob.Send(new SimulationOwnershipRequest(bob.SessionId, stalker.Id, SimulationLockType.EXCLUSIVE));
        (await bob.WaitForAsync<SimulationOwnershipResponse>()).LockAcquired.Should().BeTrue();

        alice.Send(new CreatureAttackedPlayer(stalker.Id, bob.SessionId, 20, 0));
        bob.Send(new CreatureAttackedPlayer(stalker.Id, alice.SessionId, 20, 0));
        CreatureAttackedPlayer bite = await alice.WaitForAsync<CreatureAttackedPlayer>();
        bite.CreatureId.Should().Be(stalker.Id);
        await Task.Delay(500);

        bob.ReceivedOf<CreatureAttackedPlayer>().Should().BeEmpty("Alice no longer simulates the stalker");
    }

    /// <summary>
    ///     COOP_FEATURES_SPEC D4: a bite on a vehicle goes from the creature's simulator to the vehicle's lock holder (the
    ///     pilot), whose game alone changes the vehicle's health. Bites from anyone else, and bites on a vehicle the
    ///     simulator holds itself or nobody holds, aren't sent.
    /// </summary>
    [TestMethod]
    public async Task VehicleAttack_ReachesOnlyTheVehiclesLockHolder()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CreatureAttackVehicle");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        WorldEntity aliceBoneShark = await SpawnCreatureAsync(alice, new NitroxVector3(-80, -6, 440), "BoneShark");
        WorldEntity bobSeamoth = await SpawnCreatureAsync(bob, new NitroxVector3(-80, -6, 450), "Seamoth");
        WorldEntity aliceSeamoth = await SpawnCreatureAsync(alice, new NitroxVector3(-80, -6, 470), "Seamoth");

        alice.Send(new CreatureAttackedVehicle(aliceBoneShark.Id, bobSeamoth.Id, 30, 0));
        CreatureAttackedVehicle bite = await bob.WaitForAsync<CreatureAttackedVehicle>();
        bite.CreatureId.Should().Be(aliceBoneShark.Id);
        bite.VehicleId.Should().Be(bobSeamoth.Id);
        bite.Damage.Should().Be(30);

        // Bob doesn't simulate Alice's shark; Alice's own Seamoth is damaged in her game already; an unknown vehicle has
        // no lock holder. Then a valid bite: once Bob has it, the server has handled Alice's and Bob's earlier packets.
        bob.Send(new CreatureAttackedVehicle(aliceBoneShark.Id, aliceSeamoth.Id, 30, 0));
        alice.Send(new CreatureAttackedVehicle(aliceBoneShark.Id, aliceSeamoth.Id, 30, 0));
        alice.Send(new CreatureAttackedVehicle(aliceBoneShark.Id, new NitroxId(), 30, 0));
        await Task.Delay(200);
        alice.Send(new CreatureAttackedVehicle(aliceBoneShark.Id, bobSeamoth.Id, 31, 0));
        (await bob.WaitForAsync<CreatureAttackedVehicle>(p => p.Damage == 31)).VehicleId.Should().Be(bobSeamoth.Id);
        await Task.Delay(200);

        alice.ReceivedOf<CreatureAttackedVehicle>().Should().BeEmpty("Bob's packet came from a game that doesn't simulate the shark");
        bob.ReceivedOf<CreatureAttackedVehicle>().Should().HaveCount(2, "only the two bites on his own Seamoth");
        carol.ReceivedOf<CreatureAttackedVehicle>().Should().BeEmpty("Carol holds no vehicle");

        // Carol takes the Seamoth over (she gets in): the next bite is hers.
        carol.Send(new SimulationOwnershipRequest(carol.SessionId, bobSeamoth.Id, SimulationLockType.EXCLUSIVE));
        (await carol.WaitForAsync<SimulationOwnershipResponse>()).LockAcquired.Should().BeTrue();
        alice.Send(new CreatureAttackedVehicle(aliceBoneShark.Id, bobSeamoth.Id, 32, 0));
        (await carol.WaitForAsync<CreatureAttackedVehicle>()).Damage.Should().Be(32);
    }

    /// <summary>
    ///     What a game sends for a creature it spawns (console "spawn"): the spawner gets the creature's lock.
    /// </summary>
    private static async Task<WorldEntity> SpawnCreatureAsync(FakePlayer spawner, NitroxVector3 position, string techType = "Stalker")
    {
        WorldEntity creature = new(new NitroxTransform(position, NitroxQuaternion.Identity, NitroxVector3.One), 0, "creature-class-id", false, new NitroxId(),
                                   new NitroxTechType(techType), null, null, []);
        spawner.Send(new EntitySpawnedByClient(creature, false, true));
        await spawner.WaitForAsync<SimulationOwnershipChange>(change => change.Entities.Any(entity => entity.Id == creature.Id && entity.SessionId == spawner.SessionId));
        return creature;
    }
}
