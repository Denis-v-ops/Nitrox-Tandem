using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     MULTINAUGHTICA_SPEC M1, server side of the Cyclops fix: the killing blow's metadata (health 0, <c>IsDestroyed</c> false,
///     what the fixed client sends) keeps the Cyclops a live entity, relayed, saved and sent to a late joiner; only the
///     explosion's <c>IsDestroyed</c> true makes it a wreck that isn't saved. A guard on the server contract: the fix itself is
///     in the client's extractor (<c>CyclopsDestructionRulesTest</c>), so this test passes without it too.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CyclopsDyingPersistsTest
{
    private const int STANDARD_MOTOR_MODE = 1;
    private static readonly NitroxVector3 spawnPosition = new(263, -40, 64);

    [TestMethod]
    public async Task CyclopsAtZeroHealth_StaysUntilItExplodes()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CyclopsDying");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId cyclopsId = new();
        alice.Send(new EntitySpawnedByClient(NewCyclops(cyclopsId, new CyclopsMetadata(false, false, false, true, STANDARD_MOTOR_MODE, 1500, false))));
        await bob.WaitForAsync<SpawnEntities>(p => p.Entities.Exists(e => e.Id == cyclopsId));

        // The killing blow: the lock holder's game sends health 0, the explosion follows 18 s later
        CyclopsMetadata dying = new(false, false, false, true, STANDARD_MOTOR_MODE, 0, false);
        alice.Send(new EntityMetadataUpdate(cyclopsId, dying));
        EntityMetadataUpdate relayed = await bob.WaitForAsync<EntityMetadataUpdate>(p => p.Id == cyclopsId);
        relayed.NewValue.Should().BeEquivalentTo(dying, "the partner's game gets 'dying', not 'destroyed'");
        server.WorldEntityManager.GetPersistentGlobalRootEntities().Should().Contain(e => e.Id == cyclopsId);

        await server.RestartAsync();
        FakePlayer bobAgain = await server.JoinAsync("Bob");
        bobAgain.InitialSync.GlobalRootEntities.Should().ContainSingle(e => e.Id == cyclopsId)
                .Which.Metadata.Should().BeEquivalentTo(dying, "the save keeps a dying Cyclops, so a watchdog can still end it");

        // The explosion (Vehicles.BroadcastDestroyedCyclops): now it's a wreck and isn't saved
        bobAgain.Send(new EntityMetadataUpdate(cyclopsId, new CyclopsMetadata(false, false, false, true, STANDARD_MOTOR_MODE, 0, true)));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.GetEntityById<VehicleEntity>(cyclopsId).Value.Metadata is CyclopsMetadata { IsDestroyed: true },
                                           "the server stores the explosion");
        server.WorldEntityManager.GetPersistentGlobalRootEntities().Should().NotContain(e => e.Id == cyclopsId);
    }

    private static VehicleEntity NewCyclops(NitroxId id, CyclopsMetadata metadata)
    {
        return new VehicleEntity(null, 0, new NitroxTransform(spawnPosition, NitroxQuaternion.Identity, NitroxVector3.One), string.Empty, false, id, new NitroxTechType("Cyclops"), metadata);
    }
}
