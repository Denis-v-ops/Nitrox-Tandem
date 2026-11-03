using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC R1b (upstream #2859): the escape pod remembers per player who has opened each hatch once, so the
///     first-use cinematic plays once for each of them, across rejoins and restarts (L2, real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class EscapePodHatchUseTest
{
    [TestMethod]
    public async Task HatchUse_IsKeptPerPlayer_AcrossRejoinUnderANewSessionAndRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-PodHatchUse");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId podId = alice.InitialSync.AssignedEscapePodId;

        // Alice's game reports what its EscapePodMetadataExtractor reads: her own use of the bottom hatch
        alice.Send(new EntityMetadataUpdate(podId, new EscapePodMetadata(false, false, ["Alice"], [])));
        await bob.WaitForAsync<EntityMetadataUpdate>(p => p.Id == podId);
        HatchUse(server, podId).Bottom.Should().Equal("Alice");
        HatchUse(server, podId).Top.Should().BeEmpty();

        // Bob's game repairs the pod before he has used a hatch: his empty lists must not clear Alice's use
        bob.Send(new EntityMetadataUpdate(podId, new EscapePodMetadata(true, false, [], [])));
        await alice.WaitForAsync<EntityMetadataUpdate>(p => p.Id == podId);
        HatchUse(server, podId).Bottom.Should().Equal("Alice");

        // A game can only report its own player: the name it sends is replaced by the sender's
        bob.Send(new EntityMetadataUpdate(podId, new EscapePodMetadata(true, false, [], ["Carol"])));
        await alice.WaitForAsync<EntityMetadataUpdate>(p => p.Id == podId && ((EscapePodMetadata)p.NewValue).PlayersWithTopHatchUsed.Count > 0);
        HatchUse(server, podId).Top.Should().Equal("Bob");

        // Alice rejoins after someone else took her old session id: the pod still knows her by name
        alice.Dispose();
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => !playerManager.TryGetPlayerByName("Alice", out _), "the server has dropped Alice");
        FakePlayer carol = await server.JoinAsync("Carol");
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        aliceAgain.SessionId.Should().NotBe(carol.SessionId);
        PodMetadataInJoinPayload(aliceAgain, podId).PlayersWithBottomHatchUsed.Should().Equal("Alice");
        PodMetadataInJoinPayload(carol, podId).PlayersWithBottomHatchUsed.Should().NotContain("Carol", "Carol hasn't opened the hatch");

        // And after a restart (save, load)
        await server.RestartAsync();
        FakePlayer aliceAfterRestart = await server.JoinAsync("Alice");
        EscapePodMetadata afterRestart = PodMetadataInJoinPayload(aliceAfterRestart, podId);
        afterRestart.PlayersWithBottomHatchUsed.Should().Equal("Alice");
        afterRestart.PlayersWithTopHatchUsed.Should().Equal("Bob");
        afterRestart.PodRepaired.Should().BeTrue();
    }

    private static (List<string> Bottom, List<string> Top) HatchUse(ServerHarness server, NitroxId podId)
    {
        server.Get<EntityRegistry>().TryGetEntityById(podId, out EscapePodEntity pod).Should().BeTrue();
        EscapePodMetadata metadata = pod.Metadata.Should().BeOfType<EscapePodMetadata>().Subject;
        return (metadata.PlayersWithBottomHatchUsed, metadata.PlayersWithTopHatchUsed);
    }

    private static EscapePodMetadata PodMetadataInJoinPayload(FakePlayer player, NitroxId podId)
    {
        Entity pod = player.InitialSync.GlobalRootEntities.Should().ContainSingle(entity => entity.Id == podId).Subject;
        return pod.Metadata.Should().BeOfType<EscapePodMetadata>().Subject;
    }
}
