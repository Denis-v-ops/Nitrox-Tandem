using System.Threading.Tasks;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC R1a: while one player waits in bed, everyone is told who is still awake (L2, real server and
///     protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class SleepStatusTest
{
    [TestMethod]
    public async Task OneInBed_BothKnowWhoIsAwake_ThenBothInBed_SleepCompletes()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-Sleep");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new BedEnter());
        foreach (FakePlayer player in new[] { alice, bob })
        {
            SleepStatusUpdate status = await player.WaitForAsync<SleepStatusUpdate>();
            status.PlayersInBed.Should().Be(1);
            status.TotalPlayers.Should().Be(2);
            status.AllPlayersInBed.Should().BeFalse();
            status.AwakePlayerNames.Should().Equal("Bob");
        }

        alice.ClearReceived();
        bob.ClearReceived();
        bob.Send(new BedEnter());
        foreach (FakePlayer player in new[] { alice, bob })
        {
            SleepStatusUpdate status = await player.WaitForAsync<SleepStatusUpdate>();
            status.AllPlayersInBed.Should().BeTrue();
            status.AwakePlayerNames.Should().BeEmpty();
            await player.WaitForAsync<SleepComplete>();
        }
    }

    [TestMethod]
    public async Task AwakePlayerLeaves_SleeperFallsAsleep()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-SleepAwakeLeaves");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new BedEnter());
        await alice.WaitForAsync<SleepStatusUpdate>(status => status.PlayersInBed == 1);
        alice.ClearReceived();

        bob.Dispose();
        SleepStatusUpdate update = await alice.WaitForAsync<SleepStatusUpdate>();
        update.TotalPlayers.Should().Be(1);
        update.AllPlayersInBed.Should().BeTrue();
        update.AwakePlayerNames.Should().BeEmpty();
        await alice.WaitForAsync<SleepComplete>();
    }

    /// <summary>
    ///     Before R1a nothing was sent when the only sleeper left, so the last status the awake player had still counted
    ///     the sleeper in bed.
    /// </summary>
    [TestMethod]
    public async Task SleeperLeaves_AwakePlayerGetsUpdatedStatus()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-SleepSleeperLeaves");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new BedEnter());
        await bob.WaitForAsync<SleepStatusUpdate>(status => status.PlayersInBed == 1);
        bob.ClearReceived();

        alice.Dispose();
        SleepStatusUpdate update = await bob.WaitForAsync<SleepStatusUpdate>();
        update.PlayersInBed.Should().Be(0);
        update.TotalPlayers.Should().Be(1);
        update.AwakePlayerNames.Should().Equal("Bob");

        // Bob alone in bed now sleeps right away; Alice's bed slot didn't stay behind
        bob.ClearReceived();
        bob.Send(new BedEnter());
        await bob.WaitForAsync<SleepComplete>();
        server.Get<PlayerManager>().GetConnectedPlayers().Should().ContainSingle();
    }
}
