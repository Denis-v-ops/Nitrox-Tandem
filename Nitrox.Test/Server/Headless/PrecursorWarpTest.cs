using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     FIX_SPEC S6 (#2306): the server half of "warp to a partner standing in a dry precursor room". The game's
///     precursorOutOfWater only comes from trigger volumes, which a teleport skips, so the server must store the partner's
///     InPrecursor, send it with PlayerTeleported, restore the old one on /back and keep it for the next join.
///     (L2: real server and protocol. What the client does with it is checked in game by S6-PrecursorRejoin.)
/// </summary>
[TestClass]
[TestCategory("L2")]
public class PrecursorWarpTest
{
    [TestMethod]
    public async Task WarpToPartnerInPrecursor_SendsInPrecursor_BackRestoresIt_AndItSurvivesRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-PrecursorWarp");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        PlayerManager players = server.Get<PlayerManager>();
        NitroxId cyclopsId = new();

        // Alice walks through a ForceWalk door (PrecursorTracker sends the change); Bob is in a Cyclops.
        alice.Send(new UpdateInPrecursor(true));
        bob.Send(new SubRootChanged(bob.SessionId, Optional.Of(cyclopsId)));
        await ServerHarness.WaitUntilAsync(() => Find(players, "Alice").InPrecursor && Find(players, "Bob").SubRootId.HasValue, "Alice's InPrecursor and Bob's sub reached the server");

        bob.Send(new ServerCommand("warp Alice"));
        PlayerTeleported warp = await bob.WaitForAsync<PlayerTeleported>();
        warp.InPrecursor.Should().BeTrue("Bob must land out of water, like Alice");
        Find(players, "Bob").InPrecursor.Should().BeTrue();

        bob.ClearReceived();
        bob.Send(new ServerCommand("back"));
        PlayerTeleported back = await bob.WaitForAsync<PlayerTeleported>();
        back.InPrecursor.Should().BeFalse("Bob came from a Cyclops, not a precursor room");
        back.SubRootID.Should().Be(Optional.Of(cyclopsId), "back returns Bob into the sub he came from");
        Find(players, "Bob").InPrecursor.Should().BeFalse();

        await server.RestartAsync();
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        aliceAgain.InitialSync.InPrecursor.Should().BeTrue("a rejoin in the dry room must start out of water");
    }

    private static ServerPlayer Find(PlayerManager players, string name)
    {
        return players.GetAllPlayers().Single(player => player.Name == name);
    }
}
