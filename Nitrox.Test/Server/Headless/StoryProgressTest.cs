using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Helper;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     MULTINAUGHTICA_SPEC M2, the server side (L2: real server and protocol): what the story and progress fixes store and
///     what a joining player gets back after a restart.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class StoryProgressTest
{
    private static readonly NitroxVector3 swimSpot = new(-146.5f, -12.25f, 56.75f);

    /// <summary>
    ///     A respawn in the escape pod sends the pod (EscapePod_RespawnPlayer_Patch), then "no sub root"
    ///     (Player_MovePlayerToRespawnPoint_Patch): the player stays in the pod, also after a restart.
    /// </summary>
    [TestMethod]
    public async Task LifepodRespawnPackets_LeaveThePlayerInThePod()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M2-PodRespawn");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId podId = alice.InitialSync.AssignedEscapePodId;
        NitroxId aliceId = alice.InitialSync.PlayerGameObjectId;

        alice.Send(new EscapePodChanged(alice.SessionId, Optional.Of(podId)));
        alice.Send(new SubRootChanged(alice.SessionId, Optional.Empty));
        await bob.WaitForAsync<SubRootChanged>(packet => packet.SessionId == alice.SessionId);

        AssertInPod(server, aliceId, podId);
        server.Get<PlayerManager>().TryGetPlayerByName("Alice", out ServerPlayer? player).Should().BeTrue();
        player!.SubRootId.Should().Be(Optional.Of(podId));

        await server.RestartAsync();
        AssertInPod(server, aliceId, podId);
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        aliceAgain.InitialSync.PlayerSubRootId.Should().Be(Optional.Of(podId), "she rejoins in the pod");
    }

    /// <summary>
    ///     The game drops its escape pod by distance, wherever the player is: "no escape pod" after a warp into a base keeps
    ///     the base. Leaving the pod for open water still clears it.
    /// </summary>
    [TestMethod]
    public async Task NoEscapePod_KeepsASubRoot_AndClearsThePod()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M2-PodLeave");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId podId = alice.InitialSync.AssignedEscapePodId;
        NitroxId aliceId = alice.InitialSync.PlayerGameObjectId;
        NitroxId baseId = await SpawnWorldEntityAsync(server, alice, "Base");

        alice.Send(new SubRootChanged(alice.SessionId, Optional.Of(baseId)));
        alice.Send(new EscapePodChanged(alice.SessionId, Optional.Empty));
        await bob.WaitForAsync<EscapePodChanged>(packet => packet.SessionId == alice.SessionId && !packet.EscapePodId.HasValue);
        server.EntityRegistry.TryGetEntityById(aliceId, out PlayerEntity? entity).Should().BeTrue();
        entity!.ParentId.Should().Be(baseId, "the player is still in the base");

        alice.Send(new EscapePodChanged(alice.SessionId, Optional.Of(podId)));
        alice.Send(new EscapePodChanged(alice.SessionId, Optional.Empty));
        await bob.WaitForAsync<EscapePodChanged>(packet => packet.SessionId == alice.SessionId && !packet.EscapePodId.HasValue && bob.ReceivedOf<EscapePodChanged>().Count(p => !p.EscapePodId.HasValue) >= 2);
        entity.ParentId.Should().BeNull("swimming away from the pod leaves it");
    }

    /// <summary>
    ///     FIX_SPEC S13 finding: a new player's entity has the pod as parent id, so its transform needs the pod as parent too,
    ///     or the stored world position is read as local after a restart.
    /// </summary>
    [TestMethod]
    public async Task NewPlayerEntity_KeepsItsWorldPositionAcrossRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M2-NewPlayerEntity");
        FakePlayer alice = await server.JoinAsync("Alice");
        NitroxId aliceId = alice.InitialSync.PlayerGameObjectId;

        alice.Send(new PlayerMovement(alice.SessionId, swimSpot, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName("Alice", out ServerPlayer? p) && p.Position == swimSpot, "the server stores Alice's movement");

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(aliceId, out PlayerEntity? entity).Should().BeTrue();
        entity!.ParentId.Should().Be(alice.InitialSync.AssignedEscapePodId);
        NitroxVector3 position = entity.Transform.Position;
        NitroxVector3.Distance(position, swimSpot).Should().BeLessThan(0.01f, $"the entity is where Alice last was, not {position}");
    }

    /// <summary>
    ///     Two players welding one radiation leak: the later, lower report must not make the fixed leak leak again on the
    ///     server (nor reset its fix time), and isn't relayed.
    /// </summary>
    [TestMethod]
    public async Task StaleRadiationLeakHealth_IsDropped()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M2-Radiation");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        RadiationLeakEntity[] leaks = [.. server.EntityRegistry.GetEntities<RadiationLeakEntity>().OrderBy(leak => leak.ObjectIndex)];
        leaks.Should().HaveCount(11);

        alice.Send(new EntityMetadataUpdate(leaks[0].Id, new RadiationMetadata(100, 500)));
        await ServerHarness.WaitUntilAsync(() => Health(server, leaks[0].Id) == 100, "Alice fixed leak 0");
        bob.Send(new EntityMetadataUpdate(leaks[0].Id, new RadiationMetadata(95)));
        bob.Send(new EntityMetadataUpdate(leaks[1].Id, new RadiationMetadata(60)));
        await ServerHarness.WaitUntilAsync(() => Health(server, leaks[1].Id) == 60, "Bob's packets were processed");

        RadiationMetadata stored = (RadiationMetadata)Leak(server, leaks[0].Id).Metadata;
        stored.Health.Should().Be(100, "the leak stays fixed");
        stored.FixRealTime.Should().Be(500, "its fix time stays");
        await alice.WaitForAsync<EntityMetadataUpdate>(packet => packet.Id == leaks[1].Id);
        alice.ReceivedOf<EntityMetadataUpdate>().Should().NotContain(packet => packet.Id == leaks[0].Id, "Bob's stale report isn't relayed");

        await server.RestartAsync();
        ((RadiationMetadata)Leak(server, leaks[0].Id).Metadata).Health.Should().Be(100);
    }

    /// <summary>
    ///     Two players throwing different preflight switches within one round trip: both checks are kept.
    /// </summary>
    [TestMethod]
    public async Task RocketReports_AreMerged()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M2-Rocket");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId rocketId = await SpawnWorldEntityAsync(server, alice, "RocketBase");

        alice.Send(new EntityMetadataUpdate(rocketId, new RocketMetadata(5, 100, 3, 0, [1, 2, 3, 4])));
        await ServerHarness.WaitUntilAsync(() => Rocket(server, rocketId)?.PreflightChecks.Contains(4) == true, "Alice's report is stored");
        bob.Send(new EntityMetadataUpdate(rocketId, new RocketMetadata(5, 150, 0, 0.5f, [1, 2, 3, 5])));
        await ServerHarness.WaitUntilAsync(() => Rocket(server, rocketId)?.PreflightChecks.Contains(5) == true, "Bob's report is stored");

        AssertMerged(Rocket(server, rocketId)!);
        await server.RestartAsync();
        AssertMerged(Rocket(server, rocketId)!);

        static void AssertMerged(RocketMetadata rocket)
        {
            rocket.PreflightChecks.Should().Equal(1, 2, 3, 4, 5);
            rocket.CurrentStage.Should().Be(5);
            rocket.LastStageTransitionTime.Should().Be(100, "the stage didn't advance");
            rocket.ElevatorState.Should().Be(0, "the elevator is the last reporter's");
        }
    }

    /// <summary>
    ///     Disabling the gun drops the Sunbeam radio message from the queue, as every game does; the server's queue kept it,
    ///     and every join brought it back.
    /// </summary>
    [TestMethod]
    public async Task GunDisabled_DropsTheSunbeamRadioMessage()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M2-GunRadio");
        FakePlayer alice = await server.JoinAsync("Alice");
        StoryManager storyManager = server.Get<StoryManager>();

        alice.Send(new StoryGoalExecuted("RadioSunbeam4", StoryGoalExecuted.EventType.RADIO));
        await ServerHarness.WaitUntilAsync(() => storyManager.StoryGoalData.RadioQueue.Contains("RadioSunbeam4"), "the radio message is queued");
        alice.Send(new StoryGoalExecuted("Goal_Disable_Gun", StoryGoalExecuted.EventType.STORY));
        await ServerHarness.WaitUntilAsync(() => storyManager.StoryGoalData.CompletedGoals.Contains("Goal_Disable_Gun"), "the gun goal is stored");

        storyManager.StoryGoalData.RadioQueue.Should().NotContain("RadioSunbeam4");
        await server.RestartAsync();
        FakePlayer bob = await server.JoinAsync("Bob");
        bob.InitialSync.StoryGoalData.RadioQueue.Should().NotContain("RadioSunbeam4");
    }

    /// <summary>
    ///     Guard, not a fix: the goals the games now report from <c>StoryGoalManager.OnGoalComplete</c> (a Precursor door key,
    ///     a teleporter pair) need nothing new on the server: they're stored and come back with the join after a restart.
    /// </summary>
    [TestMethod]
    public async Task DirectlyCompletedGoals_ComeBackAfterRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M2-DirectGoals");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        string teleporterGoal = TeleporterGoalKey.Format("m2test");

        alice.Send(new StoryGoalExecuted("M2TestDoorKey", StoryGoalExecuted.EventType.STORY));
        alice.Send(new StoryGoalExecuted(teleporterGoal, StoryGoalExecuted.EventType.STORY));
        await bob.WaitForAsync<StoryGoalExecuted>(packet => packet.Key == teleporterGoal);

        await server.RestartAsync();
        FakePlayer bobAgain = await server.JoinAsync("Bob");
        bobAgain.InitialSync.StoryGoalData.CompletedGoals.Should().Contain(["M2TestDoorKey", teleporterGoal]);
    }

    private static void AssertInPod(ServerHarness server, NitroxId playerId, NitroxId podId)
    {
        server.EntityRegistry.TryGetEntityById(playerId, out PlayerEntity? entity).Should().BeTrue();
        entity!.ParentId.Should().Be(podId, "the player respawned in the pod");
        server.EntityRegistry.TryGetEntityById(podId, out EscapePodEntity? pod).Should().BeTrue();
        pod!.ChildEntities.Should().Contain(child => child.Id == playerId);
    }

    private static async Task<NitroxId> SpawnWorldEntityAsync(ServerHarness server, FakePlayer player, string techType)
    {
        NitroxId id = new();
        NitroxTransform transform = new(new NitroxVector3(-30, -12, 80), NitroxQuaternion.Identity, NitroxVector3.One);
        player.Send(new EntitySpawnedByClient(new WorldEntity(transform, 0, $"{techType}-class-id", false, id, new NitroxTechType(techType), null, null, []), false, false));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.TryGetEntityById(id, out WorldEntity? _), $"the server knows the {techType}");
        return id;
    }

    private static RadiationLeakEntity Leak(ServerHarness server, NitroxId id)
    {
        server.EntityRegistry.TryGetEntityById(id, out RadiationLeakEntity? leak).Should().BeTrue();
        return leak!;
    }

    private static float Health(ServerHarness server, NitroxId id) => Leak(server, id).Metadata is RadiationMetadata metadata ? metadata.Health : -1;

    private static RocketMetadata? Rocket(ServerHarness server, NitroxId id) => server.EntityRegistry.TryGetEntityById(id, out WorldEntity? rocket) ? rocket.Metadata as RocketMetadata : null;
}
