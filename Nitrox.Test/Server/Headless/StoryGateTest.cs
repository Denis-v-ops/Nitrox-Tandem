using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC G0 (and the server half of G4): story gates through the real server and protocol (L2). Without
///     <c>StoryGateReadyProcessor</c> the default processor would relay the ready to the others and nothing would start.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class StoryGateTest
{
    private static readonly NitroxVector3 inRoom = (StoryGateRules.DriveCoreRoom.Min + StoryGateRules.DriveCoreRoom.Max) / 2f;
    private static readonly NitroxVector3 outside = inRoom + new NitroxVector3(0, 500, 0);

    [TestMethod]
    public async Task OneReady_BothSeeTheStatus_BothReady_BothGetTheStart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-StoryGate");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        foreach (FakePlayer player in new[] { alice, bob })
        {
            StoryGateStatus status = await player.WaitForAsync<StoryGateStatus>();
            status.Kind.Should().Be(StoryGateKind.DISABLE_GUN);
            status.Locked.Should().BeFalse();
            status.ReadyNames.Should().Equal("Alice");
            status.MissingNames.Should().Equal("Bob");
        }
        // A renewal that changes nothing isn't answered (a welder renews every second). Alice's packets are processed in
        // order, so once the status for another gate is back, the renewal was handled.
        alice.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        alice.Send(new StoryGateReady(StoryGateKind.ROCKET_LAUNCH, true));
        await alice.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.ROCKET_LAUNCH);
        alice.ReceivedOf<StoryGateStatus>().Count(status => status.Kind == StoryGateKind.DISABLE_GUN).Should().Be(1);

        bob.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        foreach (FakePlayer player in new[] { alice, bob })
        {
            (await player.WaitForAsync<StoryGateStart>()).Kind.Should().Be(StoryGateKind.DISABLE_GUN);
            player.ReceivedOf<StoryGateStart>().Should().HaveCount(1);
            player.ReceivedOf<StoryGateReady>().Should().BeEmpty("the asks aren't relayed");
        }
    }

    [TestMethod]
    public async Task Disconnect_ClearsTheReady_AndTellsTheOthers()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-StoryGateDisconnect");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        await bob.WaitForAsync<StoryGateStatus>(status => status.ReadyNames.Length == 1);
        bob.Send(new StoryGateReady(StoryGateKind.ROCKET_LAUNCH, true));
        await alice.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.ROCKET_LAUNCH);
        bob.ClearReceived();

        alice.Dispose();
        StoryGateStatus locked = await bob.WaitForAsync<StoryGateStatus>();
        locked.Kind.Should().Be(StoryGateKind.ROCKET_LAUNCH, "Alice's own ready went with her; Bob's gate now waits for her");
        locked.Locked.Should().BeTrue();
        locked.ReadyNames.Should().Equal("Bob");
        locked.MissingNames.Should().Equal("Alice");

        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        bob.ClearReceived();
        bob.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        StoryGateStatus waiting = await aliceAgain.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.DISABLE_GUN);
        waiting.ReadyNames.Should().Equal(["Bob"], "Alice's ready from before she left is gone");
        waiting.MissingNames.Should().Equal("Alice");
        aliceAgain.ReceivedOf<StoryGateStart>().Should().BeEmpty();
    }

    [TestMethod]
    public async Task Solo_WithARoster_IsLocked_NamingTheAbsentPlayer_UntilTheyComeOnline()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-StoryGateSolo");
        server.Get<IOptions<SubnauticaServerOptions>>().Value.StoryGatePlayers = "Alice, Bob";
        FakePlayer alice = await server.JoinAsync("Alice");

        alice.Send(new StoryGateReady(StoryGateKind.SUNBEAM_RADIO, true));
        StoryGateStatus locked = await alice.WaitForAsync<StoryGateStatus>();
        locked.Kind.Should().Be(StoryGateKind.SUNBEAM_RADIO);
        locked.Locked.Should().BeTrue();
        locked.MissingNames.Should().Equal("Bob");
        alice.ReceivedOf<StoryGateStart>().Should().BeEmpty();

        // The radio is a lock only: once Bob is online the next click plays it, without Bob confirming anything.
        FakePlayer bob = await server.JoinAsync("Bob");
        alice.Send(new StoryGateReady(StoryGateKind.SUNBEAM_RADIO, true));
        await alice.WaitForAsync<StoryGateStart>();
        await bob.WaitForAsync<StoryGateStart>();
    }

    [TestMethod]
    public async Task DriveCore_StartsWhenEveryoneIsInTheRoom()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-StoryGateDriveCore");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await MoveAsync(server, alice, inRoom);
        await MoveAsync(server, bob, outside);

        alice.Send(new StoryGateReady(StoryGateKind.DRIVE_CORE, true));
        StoryGateStatus waiting = await bob.WaitForAsync<StoryGateStatus>();
        waiting.ReadyNames.Should().Equal("Alice");
        waiting.MissingNames.Should().Equal("Bob");

        await MoveAsync(server, bob, inRoom + new NitroxVector3(1, 0, 0));
        alice.Send(new StoryGateReady(StoryGateKind.DRIVE_CORE, true));
        await alice.WaitForAsync<StoryGateStart>(start => start.Kind == StoryGateKind.DRIVE_CORE);
        await bob.WaitForAsync<StoryGateStart>(start => start.Kind == StoryGateKind.DRIVE_CORE);
    }

    [TestMethod]
    public async Task OptionOff_StartsAtOnce()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-StoryGateOff");
        server.Get<IOptions<SubnauticaServerOptions>>().Value.StoryGatesEnabled = false;
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await MoveAsync(server, alice, inRoom);
        await MoveAsync(server, bob, outside);

        alice.Send(new StoryGateReady(StoryGateKind.DRIVE_CORE, true));
        await alice.WaitForAsync<StoryGateStart>();
        await bob.WaitForAsync<StoryGateStart>();
        alice.ReceivedOf<StoryGateStatus>().Should().BeEmpty();
    }

    [TestMethod]
    public async Task AdminRelease_StartsAWaitingGate_OrTheNextAsk()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-StoryGateRelease");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await MoveAsync(server, alice, inRoom);
        await MoveAsync(server, bob, outside);

        alice.Send(new StoryGateReady(StoryGateKind.DRIVE_CORE, true));
        await alice.WaitForAsync<StoryGateStatus>();
        alice.Send(new ServerCommand("storygate release drive_core"));
        await alice.WaitForAsync<StoryGateStart>(start => start.Kind == StoryGateKind.DRIVE_CORE);
        await bob.WaitForAsync<StoryGateStart>(start => start.Kind == StoryGateKind.DRIVE_CORE);

        // Released with nobody waiting: Bob's partner stopped playing for good, the next click goes through alone.
        bob.Dispose();
        alice.ClearReceived();
        alice.Send(new ServerCommand("storygate release disable_gun"));
        await ServerHarness.WaitUntilAsync(() => server.Get<StoryGateService>().DescribeGates().Any(line => line.StartsWith("DISABLE_GUN") && line.Contains("released")),
                                           "the release is noted");
        alice.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        await alice.WaitForAsync<StoryGateStart>(start => start.Kind == StoryGateKind.DISABLE_GUN);
        alice.ReceivedOf<StoryGateStatus>().Should().BeEmpty();
    }

    private static async Task MoveAsync(ServerHarness server, FakePlayer player, NitroxVector3 position)
    {
        player.Send(new PlayerMovement(player.SessionId, position, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName(player.Name, out ServerPlayer? p) && p.Position == position,
                                           $"the server has {player.Name}'s position");
    }
}
