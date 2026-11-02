using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Services;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC T1: the server owns the rescue window (L2, real server and protocol). It grants a window or
///     refuses it, lets it run out, accepts revives only from a partner who can reach a downed player, uses up the kit,
///     shrinks the window on repeat downs until the player rests, and keeps the window (paused) while the player is
///     offline and across a restart (tested with the downed player leaving first). Without the processors the default one would only relay the packets.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class RescueWindowTest
{
    private static readonly NitroxVector3 spot = new(-60, -6, 380);

    [TestMethod]
    public async Task Window_IsSentToEveryone_AndRunsOut()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RescueExpires");
        (FakePlayer alice, FakePlayer bob) = await JoinTwoAsync(server);
        Options(server).RescueWindowSeconds = 2;

        alice.Send(new PlayerDowned(bob.SessionId, 999));

        PlayerDowned downed = await bob.WaitForAsync<PlayerDowned>();
        downed.PlayerId.Should().Be(alice.SessionId, "the server takes the sender, not the packet's field");
        downed.WindowSeconds.Should().Be(2);
        (await alice.WaitForAsync<PlayerDowned>()).WindowSeconds.Should().Be(2, "the downed player's own game gets the window too");
        Player(server, "Alice").DownedUntil.Should().NotBeNull();

        (await bob.WaitForAsync<PlayerRescueExpired>(timeout: TimeSpan.FromSeconds(10))).PlayerId.Should().Be(alice.SessionId);
        (await alice.WaitForAsync<PlayerRescueExpired>()).PlayerId.Should().Be(alice.SessionId, "Alice's game lets the kill through on this");
        Player(server, "Alice").DownedUntil.Should().BeNull();
    }

    [TestMethod]
    public async Task Revive_IsOnlyAccepted_FromAPartnerWhoCanReach_ADownedPlayer()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RescueRevive");
        (FakePlayer alice, FakePlayer bob) = await JoinTwoAsync(server);
        FakePlayer carol = await server.JoinAsync("Carol");
        await MoveAsync(server, carol, spot + new NitroxVector3(10, 0, 0));
        PlayerReviveRequest byHand = new(alice.SessionId, PlayerReviveRequest.ReviveMethod.HAND, Optional.Empty);

        // Not down: nothing to revive
        bob.Send(byHand);
        await alice.AssertNotReceivedAsync<PlayerRevived>(quietPeriod: TimeSpan.FromSeconds(1));

        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>();

        alice.Send(byHand);
        carol.Send(byHand);
        await bob.AssertNotReceivedAsync<PlayerRevived>(quietPeriod: TimeSpan.FromSeconds(1));
        Player(server, "Alice").DownedUntil.Should().NotBeNull("nobody revives themselves, and Carol is 10 m away");

        bob.Send(byHand);
        PlayerRevived revived = await carol.WaitForAsync<PlayerRevived>();
        revived.PlayerId.Should().Be(alice.SessionId);
        revived.RescuerId.Should().Be(bob.SessionId);
        revived.Health.Should().Be(RescueRules.HAND_HEALTH);
        (await alice.WaitForAsync<PlayerRevived>()).Health.Should().Be(RescueRules.HAND_HEALTH);
        Player(server, "Alice").DownedUntil.Should().BeNull();

        // A downed player can't revive anyone
        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>(packet => packet.PlayerId == alice.SessionId);
        Player(server, "Bob").DownedUntil = 1e9;
        alice.ClearReceived();
        bob.ClearReceived();
        bob.Send(byHand);
        await alice.AssertNotReceivedAsync<PlayerRevived>(quietPeriod: TimeSpan.FromSeconds(1));
        bob.ReceivedOf<PlayerRevived>().Should().BeEmpty();
    }

    [TestMethod]
    public async Task KitRevive_UsesUpTheKit_ForEveryone()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RescueKit");
        (FakePlayer alice, FakePlayer bob) = await JoinTwoAsync(server);
        NitroxId kitId = new();
        NitroxId alicesKitId = new();
        await PickUpAsync(server, bob, alice, Item(kitId, "FirstAidKit", bob.InitialSync.PlayerGameObjectId));
        await PickUpAsync(server, alice, bob, Item(alicesKitId, "FirstAidKit", alice.InitialSync.PlayerGameObjectId));
        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>();

        // Not Bob's kit, or no kit at all: refused
        bob.Send(new PlayerReviveRequest(alice.SessionId, PlayerReviveRequest.ReviveMethod.FIRST_AID_KIT, Optional.Of(alicesKitId)));
        bob.Send(new PlayerReviveRequest(alice.SessionId, PlayerReviveRequest.ReviveMethod.FIRST_AID_KIT, Optional.Empty));
        await alice.AssertNotReceivedAsync<PlayerRevived>(quietPeriod: TimeSpan.FromSeconds(1));

        bob.Send(new PlayerReviveRequest(alice.SessionId, PlayerReviveRequest.ReviveMethod.FIRST_AID_KIT, Optional.Of(kitId)));
        (await alice.WaitForAsync<PlayerRevived>()).Health.Should().Be(RescueRules.FIRST_AID_KIT_HEALTH);
        (await alice.WaitForAsync<EntityDestroyed>()).Id.Should().Be(kitId, "Alice's game removes its copy of Bob's kit");
        (await bob.WaitForAsync<EntityDestroyed>()).Id.Should().Be(kitId, "Bob's game removes the kit from his inventory");
        server.EntityRegistry.TryGetEntityById(kitId, out Entity _).Should().BeFalse();
        server.EntityRegistry.TryGetEntityById(bob.InitialSync.PlayerGameObjectId, out PlayerEntity bobEntity).Should().BeTrue();
        bobEntity.ChildEntities.Should().NotContain(child => child.Id == kitId);
        server.EntityRegistry.TryGetEntityById(alicesKitId, out Entity _).Should().BeTrue("a refused kit stays");

        // The kit is gone: the same request again does nothing
        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>(packet => packet.WindowSeconds < 60);
        alice.ClearReceived();
        bob.Send(new PlayerReviveRequest(alice.SessionId, PlayerReviveRequest.ReviveMethod.FIRST_AID_KIT, Optional.Of(kitId)));
        await alice.AssertNotReceivedAsync<PlayerRevived>(quietPeriod: TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task Window_EndsAtOnce_WhenNobodyIsUpToHelp()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RescueNobody");
        FakePlayer alice = await server.JoinAsync("Alice");

        // Alone: no window
        alice.Send(new PlayerDowned(default, 0));
        (await alice.WaitForAsync<PlayerRescueExpired>()).PlayerId.Should().Be(alice.SessionId);
        alice.ReceivedOf<PlayerDowned>().Should().BeEmpty();
        Player(server, "Alice").DownsSinceRest.Should().Be(0, "a down nobody could help with doesn't count");

        // Everyone down: both windows end
        FakePlayer bob = await server.JoinAsync("Bob");
        alice.ClearReceived();
        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>();
        bob.Send(new PlayerDowned(default, 0));
        (await alice.WaitForAsync<PlayerRescueExpired>(packet => packet.PlayerId == alice.SessionId)).Should().NotBeNull();
        (await alice.WaitForAsync<PlayerRescueExpired>(packet => packet.PlayerId == bob.SessionId)).Should().NotBeNull();
        bob.ReceivedOf<PlayerDowned>().Should().NotContain(packet => packet.PlayerId == bob.SessionId);
        Player(server, "Alice").DownedUntil.Should().BeNull();
        Player(server, "Bob").DownedUntil.Should().BeNull();

        // The last player who is up leaves: the downed one's window ends
        alice.ClearReceived();
        bob.ClearReceived();
        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>(packet => packet.PlayerId == alice.SessionId);
        bob.Dispose();
        (await alice.WaitForAsync<PlayerRescueExpired>()).PlayerId.Should().Be(alice.SessionId);
        Player(server, "Alice").DownedUntil.Should().BeNull();
    }

    [TestMethod]
    public async Task Death_EndsTheWindow()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RescueDeath");
        (FakePlayer alice, FakePlayer bob) = await JoinTwoAsync(server);

        // Her game's death event (e.g. after drowning while down)
        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>();
        alice.Send(new PlayerDeathEvent(alice.SessionId, spot));
        (await bob.WaitForAsync<PlayerRescueExpired>()).PlayerId.Should().Be(alice.SessionId);
        Player(server, "Alice").DownedUntil.Should().BeNull();

        // Her game saying she died while down
        bob.ClearReceived();
        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>();
        alice.Send(new PlayerRescueExpired(bob.SessionId));
        (await bob.WaitForAsync<PlayerRescueExpired>()).PlayerId.Should().Be(alice.SessionId, "the server takes the sender");
        Player(server, "Alice").DownedUntil.Should().BeNull();
        Player(server, "Bob").DownedUntil.Should().BeNull();
        alice.ReceivedOf<PlayerRescueExpired>().Should().BeEmpty("her own game already let the kill through");
    }

    [TestMethod]
    public async Task Escalation_HalvesThenRefuses_AndASleepOr15MinutesResetIt()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RescueEscalation");
        (FakePlayer alice, FakePlayer bob) = await JoinTwoAsync(server);
        ServerPlayer aliceOnServer = Player(server, "Alice");

        (await DownAsync(alice, bob)).Should().Be(60, "the first down gets the full window");
        await ReviveAsync(alice, bob);
        (await DownAsync(alice, bob)).Should().Be(30, "the second gets half");
        await ReviveAsync(alice, bob);
        (await DownAsync(alice, bob)).Should().Be(0, "the third gets none: the kill goes through");
        aliceOnServer.DownsSinceRest.Should().Be(3);

        // A completed sleep with Alice in bed
        alice.Send(new BedEnter());
        bob.Send(new BedEnter());
        await alice.WaitForAsync<SleepComplete>(timeout: TimeSpan.FromSeconds(15));
        await ServerHarness.WaitUntilAsync(() => aliceOnServer.DownsSinceRest == 0, "the sleep reset Alice's count");
        (await DownAsync(alice, bob)).Should().Be(60, "rested: the full window again");
        await ReviveAsync(alice, bob);
        (await DownAsync(alice, bob)).Should().Be(30);
        await ReviveAsync(alice, bob);

        // 15 min without a down
        aliceOnServer.LastDownedAt -= RescueRules.REST_AFTER_SECONDS;
        (await DownAsync(alice, bob)).Should().Be(60, "15 minutes without a down count as rest");
        aliceOnServer.DownsSinceRest.Should().Be(1);

        // Escalation off: always the full window
        await ReviveAsync(alice, bob);
        Options(server).RescueEscalation = false;
        (await DownAsync(alice, bob)).Should().Be(60);
        await ReviveAsync(alice, bob);
        (await DownAsync(alice, bob)).Should().Be(60);
    }

    [TestMethod]
    public async Task Window_PausesWhileTheDownedPlayerIsOffline_AndSurvivesARestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-RescueOffline");
        (FakePlayer alice, FakePlayer bob) = await JoinTwoAsync(server);
        TimeService time = server.Get<TimeService>();

        alice.Send(new PlayerDowned(default, 0));
        await bob.WaitForAsync<PlayerDowned>();
        double left = Player(server, "Alice").DownedUntil!.Value - time.ActiveTime.TotalSeconds;
        alice.Dispose();
        await ServerHarness.WaitUntilAsync(() => !Player(server, "Alice").IsOnline, "the server dropped Alice");

        // Bob plays on for two minutes; Alice's window doesn't run
        time.ActiveTime += TimeSpan.FromMinutes(2);
        await Task.Delay(1000);
        double leftWhileAway = Player(server, "Alice").DownedUntil!.Value - time.ActiveTime.TotalSeconds;
        leftWhileAway.Should().BeApproximately(left, 2, "the window is paused while Alice is offline");
        bob.ReceivedOf<PlayerRescueExpired>().Should().BeEmpty();

        // Saved, stopped and loaded: the count and the time left stay
        await server.RestartAsync();
        ServerPlayer reloaded = Player(server, "Alice");
        reloaded.DownsSinceRest.Should().Be(1);
        reloaded.DownedUntil.Should().NotBeNull();
        FakePlayer bobAgain = await server.JoinAsync("Bob");
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        PlayerDowned resumed = await aliceAgain.WaitForAsync<PlayerDowned>();
        resumed.PlayerId.Should().Be(aliceAgain.SessionId, "Alice rejoins still down");
        resumed.WindowSeconds.Should().BeApproximately((float)left, 3);
        (await bobAgain.WaitForAsync<PlayerDowned>()).PlayerId.Should().Be(aliceAgain.SessionId);

        // A late joiner learns about the downed partner too
        FakePlayer carol = await server.JoinAsync("Carol");
        (await carol.WaitForAsync<PlayerDowned>()).PlayerId.Should().Be(aliceAgain.SessionId);
    }

    private static async Task<float> DownAsync(FakePlayer downed, FakePlayer partner)
    {
        downed.ClearReceived();
        partner.ClearReceived();
        downed.Send(new PlayerDowned(default, 0));
        while (true)
        {
            await Task.Delay(20);
            if (downed.ReceivedOf<PlayerDowned>().FirstOrDefault() is { } granted)
            {
                return granted.WindowSeconds;
            }
            if (downed.ReceivedOf<PlayerRescueExpired>().Any())
            {
                return 0;
            }
        }
    }

    private static async Task ReviveAsync(FakePlayer downed, FakePlayer rescuer)
    {
        rescuer.Send(new PlayerReviveRequest(downed.SessionId, PlayerReviveRequest.ReviveMethod.HAND, Optional.Empty));
        await downed.WaitForAsync<PlayerRevived>();
    }

    private static async Task<(FakePlayer Alice, FakePlayer Bob)> JoinTwoAsync(ServerHarness server)
    {
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        await MoveAsync(server, alice, spot);
        await MoveAsync(server, bob, spot + new NitroxVector3(1.5f, 0, 0));
        return (alice, bob);
    }

    private static SubnauticaServerOptions Options(ServerHarness server) => server.Get<IOptions<SubnauticaServerOptions>>().Value;

    /// <summary>
    ///     The server's record of the player, online or not.
    /// </summary>
    private static ServerPlayer Player(ServerHarness server, string name) => server.Get<PlayerManager>().GetAllPlayers().Single(player => player.Name == name);

    /// <summary>
    ///     What a game sends when its player picks something up (<c>Items.PickedUp</c>); the partner gets the spawn.
    /// </summary>
    private static async Task PickUpAsync(ServerHarness server, FakePlayer picker, FakePlayer partner, InventoryItemEntity item)
    {
        picker.Send(new PickupItem(item));
        await partner.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == item.Id));
        server.EntityRegistry.TryGetEntityById(item.Id, out InventoryItemEntity _).Should().BeTrue();
    }

    private static InventoryItemEntity Item(NitroxId id, string techType, NitroxId inventoryId) => new(id, $"{techType}-class-id", new NitroxTechType(techType), null, inventoryId, []);

    private static async Task MoveAsync(ServerHarness server, FakePlayer player, NitroxVector3 position)
    {
        player.Send(new PlayerMovement(player.SessionId, position, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName(player.Name, out ServerPlayer? p) && p.Position == position,
                                           $"the server has {player.Name}'s position");
    }
}
