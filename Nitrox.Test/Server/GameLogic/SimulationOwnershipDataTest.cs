using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M5 (MultiNaughtica's hand-over grace): the server remembers who lost a lock last, and when, so
///     that the previous simulating player's last creature health report is still accepted right after a hand-over.
/// </summary>
[TestClass]
public class SimulationOwnershipDataTest
{
    private long now = 1_000_000;
    private SimulationOwnershipData data = null!;
    private Player alice = null!;
    private Player bob = null!;

    [TestInitialize]
    public void Init()
    {
        data = new SimulationOwnershipData { TickCount = () => now };
        alice = CreatePlayer(1);
        bob = CreatePlayer(2);
    }

    [TestMethod]
    public void APlayerWhoseLockWasRevoked_WasOwnerWithinTheWindow()
    {
        NitroxId id = new();
        data.TryToAcquire(id, alice, SimulationLockType.TRANSIENT);
        data.RevokeIfOwner(id, alice).Should().BeTrue();
        now += 1000;

        data.WasOwnerWithin(id, alice.SessionId, 1500).Should().BeTrue();
        data.WasOwnerWithin(id, bob.SessionId, 1500).Should().BeFalse();
        now += 600;
        data.WasOwnerWithin(id, alice.SessionId, 1500).Should().BeFalse();
    }

    /// <summary>
    ///     An exclusive lock breaks a transient one (e.g. the other player grabs the creature with the propulsion cannon):
    ///     that is a hand-over too.
    /// </summary>
    [TestMethod]
    public void APlayerWhoseTransientLockWasTakenExclusively_WasOwner()
    {
        NitroxId id = new();
        data.TryToAcquire(id, alice, SimulationLockType.TRANSIENT);
        data.TryToAcquire(id, bob, SimulationLockType.EXCLUSIVE).Should().BeTrue();

        data.WasOwnerWithin(id, alice.SessionId, 1500).Should().BeTrue();
        data.GetPlayerForLock(id).Should().BeSameAs(bob);
    }

    [TestMethod]
    public void ARevokedOwnerOfAnId_WasOwner()
    {
        NitroxId id = new();
        data.TryToAcquire(id, alice, SimulationLockType.TRANSIENT);
        data.RevokeOwnerOfId(id).Should().BeTrue();

        data.WasOwnerWithin(id, alice.SessionId, 1500).Should().BeTrue();
    }

    [TestMethod]
    public void AForgottenPreviousOwner_WasNotOwner()
    {
        NitroxId id = new();
        data.TryToAcquire(id, alice, SimulationLockType.TRANSIENT);
        data.RevokeIfOwner(id, alice);
        data.ForgetPreviousOwner(id);

        data.WasOwnerWithin(id, alice.SessionId, 1500).Should().BeFalse();
    }

    [TestMethod]
    public void APlayerWhoNeverHeldTheLock_WasNotOwner()
    {
        NitroxId id = new();
        data.TryToAcquire(id, alice, SimulationLockType.TRANSIENT);
        // Refused: Alice keeps the lock
        data.TryToAcquire(id, bob, SimulationLockType.TRANSIENT).Should().BeFalse();

        data.WasOwnerWithin(id, alice.SessionId, 1500).Should().BeFalse();
        data.WasOwnerWithin(id, bob.SessionId, 1500).Should().BeFalse();
    }

    private static Player CreatePlayer(ushort sessionId)
    {
        return new Player(sessionId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }
}
