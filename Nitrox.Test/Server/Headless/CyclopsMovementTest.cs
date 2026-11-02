using System.Collections.Generic;
using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     FIX_SPEC S13, server side of Cyclops movement: who simulates the sub while the players take turns at the helm, and
///     where the server keeps the pilot and a passenger while the sub moves. The game side (replication, pawns) is the L3
///     scenario S13-CyclopsMovement. The packets are the ones the client sends: <c>NitroxConsole.SpawnVehicle</c>,
///     <c>PilotingChair_OnHandClick_Patch</c> (exclusive), <c>PilotingChair_ReleaseBy_Patch</c> (back to transient),
///     <c>MovementBroadcaster</c> (VehicleMovements), <c>PlayerMovementBroadcaster</c> (PlayerInCyclopsMovement).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CyclopsMovementTest
{
    private static readonly NitroxVector3 spawnPosition = new(263, -40, 64);
    private static readonly NitroxVector3 drivenPosition = new(263, -40, 264);
    private static readonly NitroxVector3 passengerLocalPosition = new(0.5f, -1.2f, 4f);

    /// <summary>
    ///     The spawner simulates the sub; whoever takes the helm gets it exclusively, keeps it (transient) after leaving the
    ///     helm, loses it to the partner taking the helm, and it goes back to the partner when the holder leaves the game.
    /// </summary>
    [TestMethod]
    public async Task SimulationLock_FollowsTheHelm()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CyclopsLock");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        SimulationOwnershipData locks = server.Get<SimulationOwnershipData>();
        NitroxId cyclopsId = new();

        bob.Send(new EntitySpawnedByClient(NewCyclops(cyclopsId, spawnPosition)));
        await alice.WaitForAsync<SimulationOwnershipChange>(p => Is(p, cyclopsId, bob, SimulationLockType.TRANSIENT));
        Owner(locks, cyclopsId).Should().Be("Bob", "the spawner simulates the new sub");

        // Alice takes the helm while Bob simulates the sub (the case S6 never ran)
        alice.Send(new SimulationOwnershipRequest(alice.SessionId, cyclopsId, SimulationLockType.EXCLUSIVE));
        (await alice.WaitForAsync<SimulationOwnershipResponse>(p => p.Id == cyclopsId)).LockAcquired.Should().BeTrue();
        await bob.WaitForAsync<SimulationOwnershipChange>(p => Is(p, cyclopsId, alice, SimulationLockType.EXCLUSIVE));

        bob.Send(new SimulationOwnershipRequest(bob.SessionId, cyclopsId, SimulationLockType.EXCLUSIVE));
        (await bob.WaitForAsync<SimulationOwnershipResponse>(p => p.Id == cyclopsId)).LockAcquired.Should().BeFalse("Alice is at the helm");

        // Alice leaves the helm: she keeps simulating
        alice.ClearReceived();
        alice.Send(new SimulationOwnershipRequest(alice.SessionId, cyclopsId, SimulationLockType.TRANSIENT));
        (await alice.WaitForAsync<SimulationOwnershipResponse>(p => p.Id == cyclopsId)).LockAcquired.Should().BeTrue();
        await bob.WaitForAsync<SimulationOwnershipChange>(p => Is(p, cyclopsId, alice, SimulationLockType.TRANSIENT));

        // Bob takes the helm
        bob.ClearReceived();
        bob.Send(new SimulationOwnershipRequest(bob.SessionId, cyclopsId, SimulationLockType.EXCLUSIVE));
        (await bob.WaitForAsync<SimulationOwnershipResponse>(p => p.Id == cyclopsId)).LockAcquired.Should().BeTrue();
        await alice.WaitForAsync<SimulationOwnershipChange>(p => Is(p, cyclopsId, bob, SimulationLockType.EXCLUSIVE));
        Owner(locks, cyclopsId).Should().Be("Bob");

        // Bob leaves the game at the helm: Alice simulates the sub again
        alice.ClearReceived();
        bob.Dispose();
        await alice.WaitForAsync<SimulationOwnershipChange>(p => Is(p, cyclopsId, alice, SimulationLockType.TRANSIENT));
        Owner(locks, cyclopsId).Should().Be("Alice");
    }

    /// <summary>
    ///     The pilot's position follows the sub (steering wheel), a passenger's is kept relative to the sub, and both
    ///     reach the partner and survive a restart.
    /// </summary>
    [TestMethod]
    public async Task PilotAndPassengerPositions_FollowTheSub()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CyclopsPositions");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        PlayerManager players = server.Get<PlayerManager>();
        NitroxId cyclopsId = new();

        bob.Send(new EntitySpawnedByClient(NewCyclops(cyclopsId, spawnPosition)));
        await alice.WaitForAsync<SimulationOwnershipChange>(p => Is(p, cyclopsId, bob, SimulationLockType.TRANSIENT));
        alice.Send(new SubRootChanged(alice.SessionId, Optional.Of(cyclopsId)));
        bob.Send(new SubRootChanged(bob.SessionId, Optional.Of(cyclopsId)));
        await ServerHarness.WaitUntilAsync(() => Find(players, "Alice").SubRootId.HasValue && Find(players, "Bob").SubRootId.HasValue, "both players are in the sub");

        bob.Send(new PlayerInCyclopsMovement(bob.SessionId, passengerLocalPosition, NitroxQuaternion.Identity));
        await alice.WaitForAsync<PlayerInCyclopsMovement>(p => p.SessionId == bob.SessionId);
        Find(players, "Bob").Position.Should().Be(spawnPosition + passengerLocalPosition);

        alice.Send(new SimulationOwnershipRequest(alice.SessionId, cyclopsId, SimulationLockType.EXCLUSIVE));
        await alice.WaitForAsync<SimulationOwnershipResponse>(p => p.Id == cyclopsId && p.LockAcquired);
        alice.Send(new VehicleMovements([new DrivenVehicleMovementData(cyclopsId, drivenPosition, NitroxQuaternion.Identity, 0, 0, true)], 1));
        await bob.WaitForAsync<VehicleMovements>(p => p.Data.Exists(d => d.Id == cyclopsId));
        server.EntityRegistry.GetEntityById<VehicleEntity>(cyclopsId).Value.Transform.Position.Should().Be(drivenPosition);
        Find(players, "Alice").Position.Z.Should().BeApproximately(drivenPosition.Z - 23.54f, 0.01f, "the pilot sits at the steering wheel");
    }

    /// <summary>
    ///     A passenger who leaves the game while the partner drives on must come back inside the sub, where it is now, not
    ///     in the water where the sub was when they left.
    /// </summary>
    [TestMethod]
    public async Task PassengerRejoins_InsideTheSub_AfterThePartnerDroveOn()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CyclopsRejoin");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        PlayerManager players = server.Get<PlayerManager>();
        NitroxId cyclopsId = new();

        bob.Send(new EntitySpawnedByClient(NewCyclops(cyclopsId, spawnPosition)));
        await alice.WaitForAsync<SimulationOwnershipChange>(p => Is(p, cyclopsId, bob, SimulationLockType.TRANSIENT));
        alice.Send(new SubRootChanged(alice.SessionId, Optional.Of(cyclopsId)));
        bob.Send(new SubRootChanged(bob.SessionId, Optional.Of(cyclopsId)));
        bob.Send(new PlayerInCyclopsMovement(bob.SessionId, passengerLocalPosition, NitroxQuaternion.Identity));
        await alice.WaitForAsync<PlayerInCyclopsMovement>(p => p.SessionId == bob.SessionId);

        bob.Dispose();
        await ServerHarness.WaitUntilAsync(() => players.GetConnectedPlayers().Count == 1, "the server dropped Bob");
        alice.Send(new SimulationOwnershipRequest(alice.SessionId, cyclopsId, SimulationLockType.EXCLUSIVE));
        await alice.WaitForAsync<SimulationOwnershipResponse>(p => p.Id == cyclopsId && p.LockAcquired);
        alice.Send(new VehicleMovements([new DrivenVehicleMovementData(cyclopsId, drivenPosition, NitroxQuaternion.Identity, 0, 0, true)], 1));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.GetEntityById<VehicleEntity>(cyclopsId).Value.Transform.Position == drivenPosition, "the sub moved");

        FakePlayer bobAgain = await server.JoinAsync("Bob");
        bobAgain.InitialSync.PlayerSubRootId.Should().Be(Optional.Of(cyclopsId));
        bobAgain.InitialSync.PlayerSpawnData.Should().Be(drivenPosition + passengerLocalPosition, "Bob must come back inside the sub, which moved on");

        // Same after a restart (the save keeps the player's position)
        bobAgain.Dispose();
        await ServerHarness.WaitUntilAsync(() => players.GetConnectedPlayers().Count == 1, "the server dropped Bob again");
        await server.RestartAsync();
        FakePlayer bobAfterRestart = await server.JoinAsync("Bob");
        bobAfterRestart.InitialSync.PlayerSubRootId.Should().Be(Optional.Of(cyclopsId));
        bobAfterRestart.InitialSync.PlayerSpawnData.Should().Be(drivenPosition + passengerLocalPosition);
    }

    private static VehicleEntity NewCyclops(NitroxId id, NitroxVector3 position)
    {
        return new VehicleEntity(null, 0, new NitroxTransform(position, NitroxQuaternion.Identity, NitroxVector3.One), string.Empty, false, id, new NitroxTechType("Cyclops"), null);
    }

    private static bool Is(SimulationOwnershipChange change, NitroxId id, FakePlayer owner, SimulationLockType lockType)
    {
        return change.Entities.Exists(entity => entity.Id == id && entity.SessionId == owner.SessionId && entity.LockType == lockType);
    }

    private static string? Owner(SimulationOwnershipData locks, NitroxId id) => locks.GetPlayerForLock(id)?.Name;

    private static ServerPlayer Find(PlayerManager players, string name)
    {
        return players.GetAllPlayers().Single(player => player.Name == name);
    }
}
