using System.Net;
using System.Threading.Tasks;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.MultiplayerSession;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     MULTINAUGHTICA_SPEC M3: a player who comes back is the same player, and a game that crashed or lost the connection
///     doesn't lock its player out (L2: real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class JoinSafetyTest
{
    /// <summary>
    ///     A crash while loading after the reservation: the server drops the session, and the name must be free again, not
    ///     "in use" until the server restarts.
    /// </summary>
    [TestMethod]
    public async Task NameIsFree_AfterTheSessionDroppedBeforeJoining()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-NameFree");
        FakePlayer alice = await server.ConnectAsync("Alice");
        MultiplayerSessionReservation reservation = await alice.ReserveAsync();
        reservation.ReservationState.Should().Be(MultiplayerSessionReservationState.RESERVED);

        alice.Dispose();
        SessionManager sessionManager = server.Get<SessionManager>();
        await ServerHarness.WaitUntilAsync(() => sessionManager.GetSessionCount() == 0, "the server dropped Alice's session");

        FakePlayer aliceAgain = await server.ConnectAsync("Alice");
        MultiplayerSessionReservation again = await aliceAgain.ReserveAsync();
        again.ReservationState.Should().Be(MultiplayerSessionReservationState.RESERVED, $"nobody holds the name any more ({again.ReservationState})");
        await aliceAgain.CompleteJoinAsync();
    }

    /// <summary>
    ///     A game that crashed after joining: the server keeps its session until the connection times out (5 min in Debug).
    ///     The same player connecting again from the same address replaces it at once.
    /// </summary>
    [TestMethod]
    public async Task CrashedPlayer_RejoinsAtOnce_AsTheSamePlayer()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CrashRejoin");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId aliceId = alice.InitialSync.PlayerGameObjectId;

        alice.Crash();
        await Task.Delay(500);
        PlayerManager playerManager = server.Get<PlayerManager>();
        playerManager.TryGetPlayerByName("Alice", out _).Should().BeTrue("the server doesn't know yet that Alice's game is gone");

        // Well under the server's disconnect timeout (30 s in Release, 5 min in Debug)
        FakePlayer aliceAgain = await FakePlayer.JoinAsync("Alice", server.Port, TimeSpan.FromSeconds(10));
        try
        {
            aliceAgain.InitialSync.FirstTimeConnecting.Should().BeFalse();
            aliceAgain.InitialSync.PlayerGameObjectId.Should().Be(aliceId);
            playerManager.GetConnectedPlayers().Where(player => player.Name == "Alice").Should().ContainSingle()
                         .Which.SessionId.Should().Be(aliceAgain.SessionId);
            server.Get<SessionManager>().IsConnected(alice.SessionId).Should().BeFalse("the old session was removed");
            bob.IsConnected.Should().BeTrue("another player on the same address keeps playing");
        }
        finally
        {
            aliceAgain.Dispose();
        }
    }

    /// <summary>
    ///     A game that crashed after the reservation, before joining: the stale reservation holds the name until the
    ///     connection times out. Connecting again from the same address replaces it.
    /// </summary>
    [TestMethod]
    public async Task CrashedWhileLoading_RejoinsAtOnce()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CrashWhileLoading");
        FakePlayer alice = await server.ConnectAsync("Alice");
        (await alice.ReserveAsync()).ReservationState.Should().Be(MultiplayerSessionReservationState.RESERVED);

        alice.Crash();

        FakePlayer aliceAgain = await FakePlayer.JoinAsync("Alice", server.Port, TimeSpan.FromSeconds(10));
        aliceAgain.Dispose();
    }

    /// <summary>
    ///     A returning player whose game connects while the server still loads the world (big world, fast game): the
    ///     reservation waits for the world, and the player comes back as the saved player, not as a new one (new body, new
    ///     pod, starting items, intro).
    /// </summary>
    [TestMethod]
    public async Task ReturningPlayer_JoiningWhileTheWorldLoads_IsTheSavedPlayer()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-JoinWhileLoading");
        FakePlayer alice = await server.JoinAsync("Alice");
        alice.InitialSync.FirstTimeConnecting.Should().BeTrue();
        NitroxId aliceId = alice.InitialSync.PlayerGameObjectId;

        TaskCompletionSource worldLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await server.RestartHoldingWorldLoadAsync(worldLoad.Task);
            FakePlayer aliceAgain = await server.ConnectAsync("Alice");
            aliceAgain.SendReservationRequest();

            await aliceAgain.AssertNotReceivedAsync<MultiplayerSessionReservation>(quietPeriod: TimeSpan.FromSeconds(1));

            worldLoad.SetResult();
            await server.WaitForStartAsync();
            MultiplayerSessionReservation reservation = await aliceAgain.WaitForAsync<MultiplayerSessionReservation>();
            reservation.ReservationState.Should().Be(MultiplayerSessionReservationState.RESERVED);
            await aliceAgain.CompleteJoinAsync();

            aliceAgain.InitialSync.FirstTimeConnecting.Should().BeFalse();
            aliceAgain.InitialSync.PlayerGameObjectId.Should().Be(aliceId);
        }
        finally
        {
            worldLoad.TrySetResult();
        }
    }

    /// <summary>
    ///     The fallback at join: a reservation made before the saved players were loaded (here made directly, past the
    ///     processor's wait) still joins the saved player.
    /// </summary>
    [TestMethod]
    public async Task ReservationFromBeforeTheWorldLoaded_JoinsTheSavedPlayer()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-EarlyReservation");
        FakePlayer alice = await server.JoinAsync("Alice");
        NitroxId aliceId = alice.InitialSync.PlayerGameObjectId;

        TaskCompletionSource worldLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await server.RestartHoldingWorldLoadAsync(worldLoad.Task);
            FakePlayer aliceAgain = await server.ConnectAsync("Alice");
            IPEndPoint endPoint = server.Get<SessionManager>().GetEndPoint(aliceAgain.SessionId)!;
            MultiplayerSessionReservation reservation = server.Get<PlayerManager>().ReservePlayerContext(aliceAgain.SessionId, endPoint, new PlayerSettings(new NitroxColor(1, 1, 1)),
                                                                                                        new AuthenticationContext("Alice", Optional.Empty));
            reservation.ReservationState.Should().Be(MultiplayerSessionReservationState.RESERVED);
            server.Get<PlayerManager>().GetPlayerReservation(aliceAgain.SessionId)!.WasBrandNewPlayer.Should().BeTrue("the saved players aren't loaded yet");

            worldLoad.SetResult();
            await server.WaitForStartAsync();
            await aliceAgain.CompleteJoinAsync();

            aliceAgain.InitialSync.FirstTimeConnecting.Should().BeFalse();
            aliceAgain.InitialSync.PlayerGameObjectId.Should().Be(aliceId);
            aliceAgain.InitialSync.IntroCinematicMode.Should().Be(IntroCinematicMode.COMPLETED);
        }
        finally
        {
            worldLoad.TrySetResult();
        }
    }
}
