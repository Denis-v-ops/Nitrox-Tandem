using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC T5: holding on to the outside of a Seamoth a partner pilots (L2, real server and protocol). The
///     server grants a hitch only for a Seamoth another player pilots, close by, one hitcher at a time; tells everyone,
///     the hitcher's game included (it grabs on only then); tells players who join later; relays the hitcher's own let-go
///     with the position; and ends the hitch itself when the pilot gets out or leaves, or the Seamoth docks or is
///     destroyed. Without <c>PlayerHitchedProcessor</c> the default processor would relay every request to the others
///     (refused ones included) and never to the hitcher, and nothing would end a hitch or replay it to a late joiner.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class SeamothHitchTest
{
    private static readonly NitroxVector3 seamothPosition = new(-60, -10, 380);
    private static readonly NitroxVector3 nextToIt = seamothPosition + new NitroxVector3(2, 1, 0);

    [TestMethod]
    public async Task Hitch_IsGrantedToEveryone_OnlyForASeamothAPartnerPilots_CloseBy()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-SeamothHitch");
        (FakePlayer alice, FakePlayer bob, NitroxId seamothId) = await AlicePilotsASeamothAsync(server);
        FakePlayer carol = await server.JoinAsync("Carol");
        NitroxId cyclopsId = await SpawnAsync(server, alice, "Cyclops", seamothPosition + new NitroxVector3(0, 0, 3));

        // Refused, each followed by a quiet period: nothing reaches anyone, the asker included
        async Task AssertRefusedAsync(FakePlayer asker, NitroxId vehicleId, string because)
        {
            asker.Send(new PlayerHitched(default, vehicleId));
            foreach (FakePlayer player in new[] { alice, bob, carol })
            {
                await player.AssertNotReceivedAsync<PlayerHitched>(quietPeriod: TimeSpan.FromMilliseconds(300));
            }
            server.Get<HitchService>().TryGetHitch(asker.SessionId, out _).Should().BeFalse(because);
        }

        await MoveAsync(server, bob, seamothPosition + new NitroxVector3(20, 0, 0));
        await AssertRefusedAsync(bob, seamothId, "Bob is 20 m from the Seamoth");
        await MoveAsync(server, bob, nextToIt);
        await AssertRefusedAsync(alice, seamothId, "the pilot can't hold on to her own Seamoth");
        await AssertRefusedAsync(bob, cyclopsId, "only a Seamoth has room to hold on");
        await AssertRefusedAsync(bob, new NitroxId(), "there is no such vehicle");
        Player(server, "Bob").DownedUntil = 1e9;
        await AssertRefusedAsync(bob, seamothId, "a downed player can't hold on");
        Player(server, "Bob").DownedUntil = null;
        server.Get<IOptions<SubnauticaServerOptions>>().Value.SeamothHitch = false;
        await AssertRefusedAsync(bob, seamothId, "the option is off");
        server.Get<IOptions<SubnauticaServerOptions>>().Value.SeamothHitch = true;

        bob.Send(new PlayerHitched(carol.SessionId, seamothId));
        PlayerHitched granted = await alice.WaitForAsync<PlayerHitched>();
        granted.PlayerId.Should().Be(bob.SessionId, "the server takes the sender, not the packet's field");
        granted.VehicleId.Should().Be(seamothId);
        (await bob.WaitForAsync<PlayerHitched>()).PlayerId.Should().Be(bob.SessionId, "the hitcher's game grabs on only when the server agrees");
        (await carol.WaitForAsync<PlayerHitched>()).PlayerId.Should().Be(bob.SessionId);
        server.Get<HitchService>().TryGetHitch(bob.SessionId, out NitroxId hitched).Should().BeTrue();
        hitched.Should().Be(seamothId);

        // One hitcher per Seamoth
        alice.ClearReceived();
        bob.ClearReceived();
        carol.ClearReceived();
        await MoveAsync(server, carol, nextToIt);
        await AssertRefusedAsync(carol, seamothId, "Bob already holds on");

        // Nobody pilots it any more: the hitch ends, and a new one is refused
        alice.Send(new VehicleOnPilotModeChanged(seamothId, alice.SessionId, false));
        await bob.WaitForAsync<PlayerUnhitched>();
        bob.ClearReceived();
        await AssertRefusedAsync(bob, seamothId, "nobody pilots the Seamoth");
    }

    [TestMethod]
    public async Task LateJoiner_SeesTheHitch_AndTheHitchersLetGoIsRelayedWithThePosition()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-SeamothHitchJoin");
        (FakePlayer alice, FakePlayer bob, NitroxId seamothId) = await AlicePilotsASeamothAsync(server);
        await MoveAsync(server, bob, nextToIt);
        bob.Send(new PlayerHitched(default, seamothId));
        await alice.WaitForAsync<PlayerHitched>();

        FakePlayer carol = await server.JoinAsync("Carol");
        PlayerHitched replayed = await carol.WaitForAsync<PlayerHitched>();
        replayed.PlayerId.Should().Be(bob.SessionId);
        replayed.VehicleId.Should().Be(seamothId);
        carol.InitialSync.OtherPlayers.Single(p => p.SessionId == alice.SessionId).DrivingVehicle.Should().Be(seamothId, "the joiner also knows who pilots it");

        // The hitcher's game moved Bob along with the Seamoth and sends where he let go
        NitroxVector3 letGo = seamothPosition + new NitroxVector3(150, -5, 40);
        bob.Send(new PlayerUnhitched(alice.SessionId, letGo));
        PlayerUnhitched relayed = await alice.WaitForAsync<PlayerUnhitched>();
        relayed.PlayerId.Should().Be(bob.SessionId);
        relayed.Position.Should().Be(letGo);
        (await carol.WaitForAsync<PlayerUnhitched>()).Position.Should().Be(letGo);
        await bob.AssertNotReceivedAsync<PlayerUnhitched>();
        Player(server, "Bob").Position.Should().Be(letGo, "the server places Bob where he let go");
        server.Get<HitchService>().TryGetHitch(bob.SessionId, out _).Should().BeFalse();

        // Letting go twice relays nothing; a player who joins now sees no hitch
        alice.ClearReceived();
        bob.Send(new PlayerUnhitched(default, letGo));
        await alice.AssertNotReceivedAsync<PlayerUnhitched>();
        FakePlayer dave = await server.JoinAsync("Dave");
        await dave.AssertNotReceivedAsync<PlayerHitched>();
    }

    [TestMethod]
    public async Task Hitch_Ends_WhenThePilotGetsOut_Leaves_ItDocks_OrIsDestroyed()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-SeamothHitchRelease");
        (FakePlayer alice, FakePlayer bob, NitroxId seamothId) = await AlicePilotsASeamothAsync(server);
        FakePlayer carol = await server.JoinAsync("Carol");
        NitroxId cyclopsId = await SpawnAsync(server, alice, "Cyclops", seamothPosition + new NitroxVector3(0, 30, 0));

        // The server lets go for the hitcher: everyone gets it, the hitcher's game included, at the server's position of the hitcher
        async Task AssertReleasedAsync(string because, params FakePlayer[] told)
        {
            foreach (FakePlayer player in told)
            {
                PlayerUnhitched released = await player.WaitForAsync<PlayerUnhitched>(timeout: TimeSpan.FromSeconds(10));
                released.PlayerId.Should().Be(bob.SessionId, because);
                released.Position.Should().Be(nextToIt, because);
                player.ClearReceived();
            }
            server.Get<HitchService>().TryGetHitch(bob.SessionId, out _).Should().BeFalse(because);
        }

        async Task HitchAsync(FakePlayer pilot)
        {
            pilot.Send(new VehicleOnPilotModeChanged(seamothId, pilot.SessionId, true));
            await ServerHarness.WaitUntilAsync(() => Player(server, pilot.Name).PlayerContext!.DrivingVehicle == seamothId, $"{pilot.Name} pilots the Seamoth");
            bob.Send(new PlayerHitched(default, seamothId));
            await bob.WaitForAsync<PlayerHitched>();
            foreach (FakePlayer player in new[] { alice, bob, carol })
            {
                player.ClearReceived();
            }
        }

        await MoveAsync(server, bob, nextToIt);
        await HitchAsync(alice);
        alice.Send(new VehicleOnPilotModeChanged(seamothId, alice.SessionId, false));
        await AssertReleasedAsync("the pilot got out", alice, bob, carol);

        await HitchAsync(alice);
        alice.Send(new VehicleDocking(seamothId, cyclopsId, alice.SessionId));
        await AssertReleasedAsync("the Seamoth docks", alice, bob, carol);

        await HitchAsync(alice);
        alice.Send(new EntityDestroyed(seamothId));
        await AssertReleasedAsync("the Seamoth was destroyed", alice, bob, carol);

        // A new Seamoth, piloted by Carol, who then leaves the game
        seamothId = await SpawnAsync(server, carol, "Seamoth", seamothPosition);
        await HitchAsync(carol);
        carol.Dispose();
        await AssertReleasedAsync("the pilot left the game", alice, bob);

        // The hitcher leaving ends the hitch too (the others drop the body anyway)
        await HitchAsync(alice);
        bob.Dispose();
        await ServerHarness.WaitUntilAsync(() => !server.Get<HitchService>().TryGetHitch(bob.SessionId, out _), "the server forgot Bob's hitch");
    }

    private static async Task<(FakePlayer Alice, FakePlayer Bob, NitroxId SeamothId)> AlicePilotsASeamothAsync(ServerHarness server)
    {
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId seamothId = await SpawnAsync(server, alice, "Seamoth", seamothPosition);
        alice.Send(new VehicleOnPilotModeChanged(seamothId, alice.SessionId, true));
        await bob.WaitForAsync<VehicleOnPilotModeChanged>(p => p.VehicleId == seamothId && p.IsPiloting);
        alice.ClearReceived();
        bob.ClearReceived();
        return (alice, bob, seamothId);
    }

    private static async Task<NitroxId> SpawnAsync(ServerHarness server, FakePlayer spawner, string techType, NitroxVector3 position)
    {
        NitroxId id = new();
        VehicleEntity vehicle = new(null, 0, new NitroxTransform(position, NitroxQuaternion.Identity, NitroxVector3.One), string.Empty, false, id, new NitroxTechType(techType), null);
        spawner.Send(new EntitySpawnedByClient(vehicle));
        await ServerHarness.WaitUntilAsync(() => server.EntityRegistry.TryGetEntityById(id, out VehicleEntity _), $"the server has the {techType}");
        return id;
    }

    private static async Task MoveAsync(ServerHarness server, FakePlayer player, NitroxVector3 position)
    {
        player.Send(new PlayerMovement(player.SessionId, position, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        await ServerHarness.WaitUntilAsync(() => Player(server, player.Name).Position == position, $"the server has {player.Name}'s position");
    }

    private static ServerPlayer Player(ServerHarness server, string name)
    {
        server.Get<PlayerManager>().TryGetPlayerByName(name, out ServerPlayer? player).Should().BeTrue();
        return player!;
    }
}
