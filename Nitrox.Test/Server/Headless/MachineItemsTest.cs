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
///     S7 step 2 (#2495, #2494), the server side of what the producing game sends: a filtration machine's new item is
///     announced once as an item of the machine's container (<c>Items.ProducedInContainer</c>), and every item a bioreactor
///     burns as <see cref="EntityDestroyed" /> (<c>BaseBioReactor_ProducePower_Patch</c>).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class MachineItemsTest
{
    private static readonly NitroxVector3 machinePosition = new(-100, -31, 251);

    [TestMethod]
    public async Task ProducedItem_ReachesThePartnerInTheMachine_WithoutLock_AndSurvivesRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-MachineItems");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity machine = await SpawnMachineAsync(server, alice);
        bob.ClearReceived();

        InventoryItemEntity salt = new(new NitroxId(), "salt-class-id", new NitroxTechType("Salt"), null, machine.Id, []);
        alice.Send(new EntitySpawnedByClient(salt, false, false));

        SpawnEntities spawn = await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == salt.Id));
        spawn.Entities.Single(entity => entity.Id == salt.Id).ParentId.Should().Be(machine.Id);
        bob.ReceivedOf<SimulationOwnershipChange>().SelectMany(change => change.Entities).Should().NotContain(entity => entity.Id == salt.Id, "an item in a container needs no lock");
        server.EntityRegistry.TryGetEntityById(machine.Id, out WorldEntity serverMachine).Should().BeTrue();
        serverMachine.ChildEntities.Should().ContainSingle(entity => entity.Id == salt.Id);

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(salt.Id, out InventoryItemEntity reloaded).Should().BeTrue();
        reloaded.ParentId.Should().Be(machine.Id);
    }

    [TestMethod]
    public async Task BurnedItem_IsGoneForThePartnerAndAfterRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-MachineBurn");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        WorldEntity reactor = await SpawnMachineAsync(server, alice);
        InventoryItemEntity egg = new(new NitroxId(), "egg-class-id", new NitroxTechType("GasopodEgg"), null, reactor.Id, []);
        InventoryItemEntity keptEgg = new(new NitroxId(), "egg-class-id", new NitroxTechType("GasopodEgg"), null, reactor.Id, []);
        alice.Send(new EntitySpawnedByClient(egg, false, false));
        alice.Send(new EntitySpawnedByClient(keptEgg, false, false));
        await bob.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == keptEgg.Id));

        alice.Send(new EntityDestroyed(egg.Id));

        await bob.WaitForAsync<EntityDestroyed>(packet => packet.Id == egg.Id);
        server.EntityRegistry.TryGetEntityById(egg.Id, out Entity _).Should().BeFalse();
        server.EntityRegistry.TryGetEntityById(reactor.Id, out WorldEntity serverReactor).Should().BeTrue();
        serverReactor.ChildEntities.Should().ContainSingle().Which.Id.Should().Be(keptEgg.Id);

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(egg.Id, out Entity _).Should().BeFalse("a burned item doesn't come back");
        server.EntityRegistry.TryGetEntityById(keptEgg.Id, out InventoryItemEntity reloaded).Should().BeTrue();
        reloaded.ParentId.Should().Be(reactor.Id);
    }

    /// <summary>
    ///     A machine standing in the world (in game it is part of a base, which doesn't matter to these processors).
    /// </summary>
    private static async Task<WorldEntity> SpawnMachineAsync(ServerHarness server, FakePlayer owner)
    {
        WorldEntity machine = new(new NitroxTransform(machinePosition, NitroxQuaternion.Identity, NitroxVector3.One), 0, "machine-class-id", false, new NitroxId(),
                                  new NitroxTechType("BaseFiltrationMachine"), null, null, []);
        owner.Send(new EntitySpawnedByClient(machine, false, false));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.TryGetEntityById(machine.Id, out WorldEntity _), "the server knows the machine");
        return machine;
    }
}
