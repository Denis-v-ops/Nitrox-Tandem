using System.Threading.Tasks;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC D2: each game's signal interference flag reaches the partner, a player who joins later gets it at
///     once, and it doesn't outlive the session (L2, real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class DeepCutsLineTest
{
    [TestMethod]
    public async Task InterferenceIsRelayed_AndALateJoinerGetsItAtOnce()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-DeepCutsLine");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer carol = await server.JoinAsync("Carol");

        // Alice's game reports her in the Lost River
        alice.Send(new PlayerStats(alice.SessionId, 30, 45, 70, 50, 60, 0.2f, true));
        (await carol.WaitForAsync<PlayerStats>(p => p.SessionId == alice.SessionId)).Interfered.Should().BeTrue();

        // Bob joins between two of her PlayerStats (her game sends them every 3 s; this fake one sends no more)
        FakePlayer bob = await server.JoinAsync("Bob");
        PlayerStats replayed = await bob.WaitForAsync<PlayerStats>(p => p.SessionId == alice.SessionId);
        replayed.Interfered.Should().BeTrue("the late joiner's marker must show the last contact, not a live position");
        replayed.Oxygen.Should().Be(30);
        replayed.Health.Should().Be(70);
        replayed.InfectionAmount.Should().Be(0.2f);
        alice.ReceivedOf<PlayerStats>().Should().NotContain(p => p.SessionId == alice.SessionId, "nobody gets their own stats back");

        // Contact returns: her next report reaches both
        alice.Send(new PlayerStats(alice.SessionId, 30, 45, 70, 50, 60, 0.2f, false));
        (await bob.WaitForAsync<PlayerStats>(p => p.SessionId == alice.SessionId && !p.Interfered)).Interfered.Should().BeFalse();
        (await carol.WaitForAsync<PlayerStats>(p => p.SessionId == alice.SessionId && !p.Interfered)).Interfered.Should().BeFalse();
    }

    [TestMethod]
    public async Task InterferenceIsResetWhenThePlayerLeavesAndRejoins()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-DeepCutsLineRejoin");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        PlayerManager playerManager = server.Get<PlayerManager>();
        ServerPlayer? aliceOnServer = null;
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName("Alice", out aliceOnServer), "Alice is on the server");

        alice.Send(new PlayerStats(alice.SessionId, 45, 45, 80, 50, 90, 0, true));
        await bob.WaitForAsync<PlayerStats>(p => p.SessionId == alice.SessionId);
        aliceOnServer!.Interfered.Should().BeTrue();

        alice.Dispose();
        await ServerHarness.WaitUntilAsync(() => !playerManager.TryGetPlayerByName("Alice", out _), "the server has dropped Alice");
        aliceOnServer.Interfered.Should().BeFalse("an offline player's flag is stale");

        // Set it again behind the server's back: a rejoin must start clean even if the disconnect missed it
        aliceOnServer.Interfered = true;
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        aliceOnServer.Interfered.Should().BeFalse("her game hasn't reported anything yet in this session");

        FakePlayer carol = await server.JoinAsync("Carol");
        PlayerStats replayed = await carol.WaitForAsync<PlayerStats>(p => p.SessionId == aliceAgain.SessionId);
        replayed.Interfered.Should().BeFalse("a stale true would show her as lost right after her rejoin");
    }
}
