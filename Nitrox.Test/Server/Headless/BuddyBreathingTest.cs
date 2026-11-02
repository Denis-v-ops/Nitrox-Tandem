using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC T2: buddy breathing goes through the server, which gives the target alone a clamped, scaled
///     amount and drops shares the partner-interaction check refuses (L2, real server and protocol). Without
///     <c>OxygenShareProcessor</c> the default processor would relay the donor's packet unchanged to everyone.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class BuddyBreathingTest
{
    private static readonly NitroxVector3 spot = new(-60, -6, 380);

    [TestMethod]
    public async Task Share_ReachesOnlyTheTarget_AtTheServersEfficiency()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-BuddyBreathing");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        await MoveAsync(server, alice, spot);
        await MoveAsync(server, bob, spot + new NitroxVector3(1.5f, 0, 0));
        await MoveAsync(server, carol, spot + new NitroxVector3(0, 0, 1.5f));

        // The donor field is the server's to set: Alice claiming to be Carol changes nothing.
        alice.Send(new OxygenShare(carol.SessionId, bob.SessionId, 1f));
        OxygenShare received = await bob.WaitForAsync<OxygenShare>();
        received.TargetId.Should().Be(bob.SessionId);
        received.DonorId.Should().Be(alice.SessionId);
        received.Amount.Should().Be(0.5f, "BuddyBreathingEfficiency defaults to 0.5");

        server.Get<IOptions<SubnauticaServerOptions>>().Value.BuddyBreathingEfficiency = 1f;
        alice.Send(new OxygenShare(alice.SessionId, bob.SessionId, 50f));
        (await bob.WaitForAsync<OxygenShare>(share => share != received)).Amount.Should().Be(OxygenShare.MAX_AMOUNT, "the server clamps what one packet carries");

        await carol.AssertNotReceivedAsync<OxygenShare>();
        alice.ReceivedOf<OxygenShare>().Should().BeEmpty();
        bob.ReceivedOf<OxygenShare>().Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Share_IsDropped_WhenTheCheckRefusesIt()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-BuddyBreathingRefused");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        FakePlayer carol = await server.JoinAsync("Carol");
        await MoveAsync(server, alice, spot);
        await MoveAsync(server, bob, spot + new NitroxVector3(10, 0, 0));
        await MoveAsync(server, carol, spot + new NitroxVector3(0, 0, 1.5f));
        PlayerManager playerManager = server.Get<PlayerManager>();
        playerManager.TryGetPlayerByName("Alice", out ServerPlayer? aliceOnServer).Should().BeTrue();
        playerManager.TryGetPlayerByName("Carol", out ServerPlayer? carolOnServer).Should().BeTrue();

        // Each refused share is followed by a valid one for Carol (always 0.1 → 0.05): packets from Alice reach Carol in
        // order, so a refused share that got through would arrive before it.
        async Task AssertRefusedAsync(OxygenShare refused, string because)
        {
            alice.Send(refused);
            alice.Send(new OxygenShare(alice.SessionId, carol.SessionId, 0.1f));
            OxygenShare first = await carol.WaitForAsync<OxygenShare>();
            first.Amount.Should().Be(0.05f, because);
            carol.ClearReceived();
        }

        await AssertRefusedAsync(new OxygenShare(alice.SessionId, bob.SessionId, 1f), "Bob is 10 m away");
        await AssertRefusedAsync(new OxygenShare(alice.SessionId, 999, 1f), "nobody has session 999");
        await AssertRefusedAsync(new OxygenShare(alice.SessionId, alice.SessionId, 1f), "nobody shares with themselves");
        await AssertRefusedAsync(new OxygenShare(alice.SessionId, carol.SessionId, -1f), "a negative amount takes nothing");
        await AssertRefusedAsync(new OxygenShare(alice.SessionId, carol.SessionId, float.NaN), "NaN is no amount");

        // A downed partner can be given air; a downed donor can't give it.
        carolOnServer!.DownedUntil = 1000;
        alice.Send(new OxygenShare(alice.SessionId, carol.SessionId, 1f));
        (await carol.WaitForAsync<OxygenShare>()).Amount.Should().Be(0.5f, "air for a downed partner is the point");
        carolOnServer.DownedUntil = null;
        carol.ClearReceived();
        // No sentinel here: every share from a downed Alice is refused (the L1 test covers the rule itself).
        aliceOnServer!.DownedUntil = 1000;
        alice.Send(new OxygenShare(alice.SessionId, carol.SessionId, 1f));
        await carol.AssertNotReceivedAsync<OxygenShare>(quietPeriod: TimeSpan.FromSeconds(1));
        aliceOnServer.DownedUntil = null;

        bob.ReceivedOf<OxygenShare>().Should().BeEmpty();
        alice.ReceivedOf<OxygenShare>().Should().BeEmpty();
    }

    private static async Task MoveAsync(ServerHarness server, FakePlayer player, NitroxVector3 position)
    {
        player.Send(new PlayerMovement(player.SessionId, position, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName(player.Name, out ServerPlayer? p) && p.Position == position,
                                           $"the server has {player.Name}'s position");
    }
}
