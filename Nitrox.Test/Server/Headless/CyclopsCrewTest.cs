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
///     COOP_FEATURES_SPEC T4 (Cyclops crew roles), server side: what the crew member who isn't steering sets on the helm
///     screen reaches the pilot, is stored and survives a restart, although the pilot holds the sub's lock. The packets are
///     the ones the client sends: <c>CyclopsMetadata</c> from the ability buttons' patches (sonar, shield, silent running),
///     <c>CyclopsDecoyLaunch</c> and <c>CyclopsFireSuppression</c> (no server processor, relayed as they are). The game
///     side (who may click what, power drain) is the L3 scenario T4-CyclopsCrew.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CyclopsCrewTest
{
    private const int STANDARD_MOTOR_MODE = 1; // CyclopsMotorMode.CyclopsMotorModes.Standard
    private static readonly NitroxVector3 spawnPosition = new(263, -40, 64);

    [TestMethod]
    public async Task CrewMembersAbilities_ReachThePilot_AndSurviveRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CyclopsCrew");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId cyclopsId = new();

        bob.Send(new EntitySpawnedByClient(NewCyclops(cyclopsId)));
        await alice.WaitForAsync<SimulationOwnershipChange>(p => p.Entities.Exists(e => e.Id == cyclopsId && e.SessionId == bob.SessionId));
        alice.Send(new SubRootChanged(alice.SessionId, Optional.Of(cyclopsId)));
        bob.Send(new SubRootChanged(bob.SessionId, Optional.Of(cyclopsId)));

        // Alice takes the helm: she simulates the sub, Bob stands behind her
        alice.Send(new SimulationOwnershipRequest(alice.SessionId, cyclopsId, SimulationLockType.EXCLUSIVE));
        (await alice.WaitForAsync<SimulationOwnershipResponse>(p => p.Id == cyclopsId)).LockAcquired.Should().BeTrue();
        await bob.WaitForAsync<SimulationOwnershipChange>(p => p.Entities.Exists(e => e.Id == cyclopsId && e.SessionId == alice.SessionId));
        server.Get<SimulationOwnershipData>().GetPlayerForLock(cyclopsId)!.Name.Should().Be("Alice");

        // Bob turns on silent running, the shield and the sonar (one metadata update per button, as the game sends them)
        CyclopsMetadata silentRunning = new(true, false, false, true, STANDARD_MOTOR_MODE, 1500, false);
        CyclopsMetadata shield = new(true, true, false, true, STANDARD_MOTOR_MODE, 1500, false);
        CyclopsMetadata sonar = new(true, true, true, true, STANDARD_MOTOR_MODE, 1500, false);
        foreach (CyclopsMetadata metadata in new[] { silentRunning, shield, sonar })
        {
            alice.ClearReceived();
            bob.Send(new EntityMetadataUpdate(cyclopsId, metadata));
            EntityMetadataUpdate received = await alice.WaitForAsync<EntityMetadataUpdate>(p => p.Id == cyclopsId);
            received.NewValue.Should().BeEquivalentTo(metadata, "the pilot's game applies what the crew member set");
            server.EntityRegistry.GetEntityById<VehicleEntity>(cyclopsId).Value.Metadata.Should().BeEquivalentTo(metadata, "the server stores it");
        }

        // Decoy and fire suppression are one-off packets, relayed to the pilot
        bob.Send(new CyclopsDecoyLaunch(cyclopsId));
        await alice.WaitForAsync<CyclopsDecoyLaunch>(p => p.Id == cyclopsId);
        bob.Send(new CyclopsFireSuppression(cyclopsId));
        await alice.WaitForAsync<CyclopsFireSuppression>(p => p.Id == cyclopsId);

        // After a restart, a joining player gets the abilities as Bob left them
        await server.RestartAsync();
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        aliceAgain.InitialSync.GlobalRootEntities.Should().ContainSingle(e => e.Id == cyclopsId)
                  .Which.Metadata.Should().BeEquivalentTo(sonar);
    }

    private static VehicleEntity NewCyclops(NitroxId id)
    {
        CyclopsMetadata engineOn = new(false, false, false, true, STANDARD_MOTOR_MODE, 1500, false);
        return new VehicleEntity(null, 0, new NitroxTransform(spawnPosition, NitroxQuaternion.Identity, NitroxVector3.One), string.Empty, false, id, new NitroxTechType("Cyclops"), engineOn);
    }
}
