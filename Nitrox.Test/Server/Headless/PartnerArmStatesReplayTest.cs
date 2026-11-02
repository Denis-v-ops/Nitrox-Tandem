using System.Threading.Tasks;
using Nitrox.Model.GameLogic.PlayerAnimation;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC R2b: a player who joins later sees the partner's arm states, but not a fall that is long over
///     (L2, real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class PartnerArmStatesReplayTest
{
    [TestMethod]
    public async Task LateJoiner_GetsTheArmStates_NotTheFall()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-ArmStatesReplay");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer carol = await server.JoinAsync("Carol");

        // What Alice's game sends: her body pose, then her arm states once at the start and on change. She jumped off a
        // ledge (the fall went on and off), and now swims at the surface with the PDA open.
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.UNDERWATER, AnimChangeState.ON)));
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.HOLDING_TOOL, AnimChangeState.ON)));
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.JUMP, AnimChangeState.ON)));
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.DIVING, AnimChangeState.ON)));
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.ON_SURFACE, AnimChangeState.ON)));
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.USING_PDA, AnimChangeState.ON)));
        alice.Send(new AnimationChangeEvent(alice.SessionId, new PlayerAnimation(AnimChangeType.HOLDING_TOOL, AnimChangeState.OFF)));
        await carol.WaitForAsync<AnimationChangeEvent>(p => p.Animation.Type == AnimChangeType.HOLDING_TOOL && p.Animation.State == AnimChangeState.OFF);
        carol.ReceivedOf<AnimationChangeEvent>().Should().Contain(p => p.Animation.Type == AnimChangeType.JUMP, "the fall is relayed live");

        FakePlayer bob = await server.JoinAsync("Bob");

        bob.InitialSync.OtherPlayers.Single(p => p.PlayerName == "Alice").Animation.Type.Should().Be(AnimChangeType.UNDERWATER, "the join payload's pose is the body pose");
        AnimationChangeEvent pda = await bob.WaitForAsync<AnimationChangeEvent>(p => p.SessionId == alice.SessionId && p.Animation.Type == AnimChangeType.USING_PDA);
        pda.Animation.State.Should().Be(AnimChangeState.ON);
        AnimationChangeEvent surface = await bob.WaitForAsync<AnimationChangeEvent>(p => p.SessionId == alice.SessionId && p.Animation.Type == AnimChangeType.ON_SURFACE);
        surface.Animation.State.Should().Be(AnimChangeState.ON);
        AnimationChangeEvent holding = await bob.WaitForAsync<AnimationChangeEvent>(p => p.SessionId == alice.SessionId && p.Animation.Type == AnimChangeType.HOLDING_TOOL);
        holding.Animation.State.Should().Be(AnimChangeState.OFF, "only the latest state per type is kept");
        await bob.WaitForAsync<AnimationChangeEvent>(p => p.SessionId == alice.SessionId && p.Animation.Type == AnimChangeType.UNDERWATER);

        List<AnimationChangeEvent> fromAlice = bob.ReceivedOf<AnimationChangeEvent>().Where(p => p.SessionId == alice.SessionId).ToList();
        fromAlice.Should().NotContain(p => p.Animation.Type == AnimChangeType.JUMP || p.Animation.Type == AnimChangeType.DIVING,
                                      "a fall is over long before anyone joins; replayed, it would play again on her body");
        fromAlice.Should().HaveCount(4, "one per kept type: underwater, holding_tool, on_surface, using_pda");
    }
}
