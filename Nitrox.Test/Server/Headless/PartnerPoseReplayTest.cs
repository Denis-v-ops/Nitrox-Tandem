using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.GameLogic.PlayerAnimation;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC R2a: a player who joins later sees the partner's pose and held item (L2, real server and
///     protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class PartnerPoseReplayTest
{
    [TestMethod]
    public async Task LateJoiner_GetsEveryAnimationStateAndTheHeldItem()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-PoseReplay");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer carol = await server.JoinAsync("Carol");
        NitroxId flashlightId = new();

        // Standing in a base when the infection reveal plays, flashlight in hand. The reveal came last, so the server's
        // single "last animation" kept only it and lost the standing pose.
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.UNDERWATER, AnimChangeState.OFF)));
        alice.Send(new PlayerHeldItemChanged(alice.SessionId, flashlightId, PlayerHeldItemChanged.ChangeType.DRAW_AS_TOOL, new NitroxTechType("Flashlight")));
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.INFECTION_REVEAL, AnimChangeState.ON)));
        await carol.WaitForAsync<AnimationChangeEvent>(p => p.Animation.Type == AnimChangeType.INFECTION_REVEAL);

        FakePlayer bob = await server.JoinAsync("Bob");

        bob.InitialSync.OtherPlayers.Single(p => p.PlayerName == "Alice").Animation.Type.Should().Be(AnimChangeType.UNDERWATER, "the join payload's pose is a state");
        AnimationChangeEvent underwater = await bob.WaitForAsync<AnimationChangeEvent>(p => p.SessionId == alice.SessionId && p.Animation.Type == AnimChangeType.UNDERWATER);
        underwater.Animation.State.Should().Be(AnimChangeState.OFF, "Alice stands, she doesn't swim");
        PlayerHeldItemChanged held = await bob.WaitForAsync<PlayerHeldItemChanged>(p => p.SessionId == alice.SessionId);
        held.ItemId.Should().Be(flashlightId);
        held.Type.Should().Be(PlayerHeldItemChanged.ChangeType.DRAW_AS_TOOL);
        held.IsFirstTime.Should().BeNull("the first-use animation must not play again for a late joiner");
        bob.ReceivedOf<AnimationChangeEvent>().Should().NotContain(p => p.Animation.Type == AnimChangeType.INFECTION_REVEAL,
                                                                    "the reveal is a one-time animation, not a pose");
    }

    [TestMethod]
    public async Task PutAwayItem_IsNotReplayed_AndARejoinStartsEmpty()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-PoseReplayHolster");
        FakePlayer alice = await server.JoinAsync("Alice");
        NitroxId scannerId = new();

        alice.Send(new PlayerHeldItemChanged(alice.SessionId, scannerId, PlayerHeldItemChanged.ChangeType.DRAW_AS_TOOL, null));
        alice.Send(new PlayerHeldItemChanged(alice.SessionId, scannerId, PlayerHeldItemChanged.ChangeType.HOLSTER_AS_TOOL, null));
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.UNDERWATER, AnimChangeState.ON)));
        ServerPlayer aliceOnServer = await GetPlayerAsync(server, "Alice");
        await ServerHarness.WaitUntilAsync(() => aliceOnServer.Animations.ContainsKey(AnimChangeType.UNDERWATER), "the server has Alice's pose");
        aliceOnServer.HeldItem.Should().BeNull("the scanner was put away");

        FakePlayer bob = await server.JoinAsync("Bob");
        await bob.WaitForAsync<AnimationChangeEvent>(p => p.SessionId == alice.SessionId);
        await bob.AssertNotReceivedAsync<PlayerHeldItemChanged>(p => p.SessionId == alice.SessionId);

        // Alice's state belongs to her session: after a rejoin her game sends it again
        alice.Send(new PlayerHeldItemChanged(alice.SessionId, scannerId, PlayerHeldItemChanged.ChangeType.DRAW_AS_TOOL, null));
        await bob.WaitForAsync<PlayerHeldItemChanged>(p => p.SessionId == alice.SessionId);
        alice.Dispose();
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => !playerManager.TryGetPlayerByName("Alice", out _), "the server has dropped Alice");
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        aliceOnServer = await GetPlayerAsync(server, "Alice");
        aliceOnServer.HeldItem.Should().BeNull();
        aliceOnServer.Animations.Should().BeEmpty();
        aliceAgain.IsConnected.Should().BeTrue();
    }

    private static async Task<ServerPlayer> GetPlayerAsync(ServerHarness server, string name)
    {
        PlayerManager playerManager = server.Get<PlayerManager>();
        ServerPlayer? player = null;
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName(name, out player), $"{name} is on the server");
        return player!;
    }
}
