using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC G1 (disabling the gun) and G2 (rocket launch together), server side (L2): both gates wait for
///     every player's click; the rocket's click only counts while its player is in the cockpit of the rocket the server
///     knows; the <see cref="RocketLaunch" /> is only relayed once the launch gate started. The game side (the held
///     clicks, both cinematics) is the L3 scenario G1G2-GunAndRocketTogether.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class GunAndRocketGateTest
{
    private static readonly NitroxVector3 rocketPosition = new(-120, 0, 520);
    private static readonly NitroxQuaternion rocketRotation = NitroxQuaternion.FromEuler(0, 90, 0);

    [TestMethod]
    public async Task DisableGun_WaitsForBothClicks_AWithdrawnClickDoesNotCount()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-GunGate");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        StoryGateStatus waiting = await bob.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.DISABLE_GUN);
        waiting.ReadyNames.Should().Equal("Alice");
        waiting.MissingNames.Should().Equal("Bob");

        // Alice walks out of the terminal's area (OnTerminalAreaExit): her click is withdrawn, Bob's alone doesn't start it.
        alice.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, false));
        bob.ClearReceived();
        bob.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        waiting = await alice.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.DISABLE_GUN && Array.IndexOf(status.ReadyNames, "Bob") >= 0);
        waiting.ReadyNames.Should().Equal("Bob");
        waiting.MissingNames.Should().Equal("Alice");
        bob.ReceivedOf<StoryGateStart>().Should().BeEmpty();

        alice.Send(new StoryGateReady(StoryGateKind.DISABLE_GUN, true));
        foreach (FakePlayer player in new[] { alice, bob })
        {
            (await player.WaitForAsync<StoryGateStart>()).Kind.Should().Be(StoryGateKind.DISABLE_GUN);
        }
    }

    [TestMethod]
    public async Task RocketLaunch_IsNotRelayed_BeforeTheGateStarts()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RocketNoRelay");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId rocketId = AddRocket(server);

        // What a game without the gate sent at its click (the default processor relayed it to everyone).
        alice.Send(new RocketLaunch(rocketId));
        // Alice's packets are handled in order: once the status for her next ask reached Bob, the launch was handled.
        alice.Send(new StoryGateReady(StoryGateKind.HATCHING, true));
        await bob.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.HATCHING);
        await bob.AssertNotReceivedAsync<RocketLaunch>();
    }

    [TestMethod]
    public async Task RocketLaunch_StartsWhenBothClickedAboard_ThenTheLaunchIsRelayed()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RocketGate");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId rocketId = AddRocket(server);
        NitroxVector3 cockpit = CockpitCentre();
        await MoveAsync(server, alice, cockpit);
        await MoveAsync(server, bob, cockpit + new NitroxVector3(1, 0, 1));

        alice.Send(new StoryGateReady(StoryGateKind.ROCKET_LAUNCH, true));
        StoryGateStatus waiting = await bob.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.ROCKET_LAUNCH);
        waiting.ReadyNames.Should().Equal("Alice");
        waiting.MissingNames.Should().Equal(["Bob"], "being aboard isn't enough, each player presses launch");
        alice.ReceivedOf<StoryGateStart>().Should().BeEmpty();

        bob.Send(new StoryGateReady(StoryGateKind.ROCKET_LAUNCH, true));
        foreach (FakePlayer player in new[] { alice, bob })
        {
            (await player.WaitForAsync<StoryGateStart>()).Kind.Should().Be(StoryGateKind.ROCKET_LAUNCH);
        }

        // Each game launches its own player and sends the launch; now it's relayed (the partner's game ignores it).
        alice.Send(new RocketLaunch(rocketId));
        (await bob.WaitForAsync<RocketLaunch>()).RocketId.Should().Be(rocketId);
        alice.ReceivedOf<RocketLaunch>().Should().BeEmpty("the launch isn't sent back to its sender");
    }

    [TestMethod]
    public async Task RocketLaunch_AClickOnlyCountsWhileAboard_CheckedFromPositions()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RocketAboard");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        AddRocket(server);
        NitroxVector3 cockpit = CockpitCentre();
        NitroxVector3 platform = rocketPosition + new NitroxVector3(0, 2, 0);
        await MoveAsync(server, alice, cockpit);
        await MoveAsync(server, bob, platform);

        alice.Send(new StoryGateReady(StoryGateKind.ROCKET_LAUNCH, true));
        await bob.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.ROCKET_LAUNCH);
        // A click from below the cockpit (a stale or forged ready) doesn't count.
        bob.ClearReceived();
        bob.Send(new StoryGateReady(StoryGateKind.ROCKET_LAUNCH, true));
        bob.Send(new StoryGateReady(StoryGateKind.HATCHING, true));
        await bob.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.HATCHING);
        bob.ReceivedOf<StoryGateStart>().Should().BeEmpty("Bob isn't aboard");
        server.Get<StoryGateService>().DescribeGates().Should().Contain(line => line.StartsWith("ROCKET_LAUNCH") && line.Contains("rocket cockpit") && line.Contains("waiting for Bob"));

        // Alice climbs down after her click: it's dropped, so Bob's click from the cockpit waits for her again.
        await MoveAsync(server, alice, platform);
        await MoveAsync(server, bob, cockpit);
        alice.ClearReceived();
        bob.Send(new StoryGateReady(StoryGateKind.ROCKET_LAUNCH, true));
        StoryGateStatus waiting = await alice.WaitForAsync<StoryGateStatus>(status => status.Kind == StoryGateKind.ROCKET_LAUNCH && Array.IndexOf(status.ReadyNames, "Bob") >= 0);
        waiting.ReadyNames.Should().Equal("Bob");
        waiting.MissingNames.Should().Equal("Alice");

        await MoveAsync(server, alice, cockpit + new NitroxVector3(-1, 0, 0));
        alice.Send(new StoryGateReady(StoryGateKind.ROCKET_LAUNCH, true));
        await alice.WaitForAsync<StoryGateStart>(start => start.Kind == StoryGateKind.ROCKET_LAUNCH);
        await bob.WaitForAsync<StoryGateStart>(start => start.Kind == StoryGateKind.ROCKET_LAUNCH);
    }

    [TestMethod]
    public async Task RocketLaunch_GatesOff_IsRelayedAtOnce()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RocketGatesOff");
        server.Get<IOptions<SubnauticaServerOptions>>().Value.StoryGatesEnabled = false;
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId rocketId = AddRocket(server);

        alice.Send(new RocketLaunch(rocketId));
        (await bob.WaitForAsync<RocketLaunch>()).RocketId.Should().Be(rocketId);
    }

    [TestMethod]
    public void RocketCockpit_TurnsWithTheRocket()
    {
        StoryGateArea area = StoryGateRules.RocketCockpit(rocketPosition, rocketRotation);
        area.Contains(CockpitCentre()).Should().BeTrue();
        (area.Max - area.Min).Should().Be(StoryGateRules.RocketCockpitHalfSize * 2);
        area.Contains(rocketPosition + new NitroxVector3(0, -20, 0)).Should().BeFalse("under the rocket isn't aboard");
    }

    /// <summary>The middle of the cockpit, worked out independently of <see cref="StoryGateRules.RocketCockpit" />.</summary>
    private static NitroxVector3 CockpitCentre()
    {
        System.Numerics.Vector3 offset = System.Numerics.Vector3.Transform((System.Numerics.Vector3)StoryGateRules.RocketCockpitOffset, (System.Numerics.Quaternion)rocketRotation);
        return rocketPosition + (NitroxVector3)offset;
    }

    /// <summary>A finished rocket as the server stores one built by a player.</summary>
    private static NitroxId AddRocket(ServerHarness server)
    {
        NitroxId id = new();
        RocketMetadata finished = new(5, 0, 0, 0, []);
        server.EntityRegistry.AddEntity(new VehicleEntity(null, 0, new NitroxTransform(rocketPosition, rocketRotation, NitroxVector3.One), string.Empty, false, id,
                                                          new NitroxTechType("RocketBase"), finished));
        return id;
    }

    private static async Task MoveAsync(ServerHarness server, FakePlayer player, NitroxVector3 position)
    {
        player.Send(new PlayerMovement(player.SessionId, position, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName(player.Name, out ServerPlayer? p) && p.Position == position,
                                           $"the server has {player.Name}'s position");
    }
}
