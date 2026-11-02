using System.Threading.Tasks;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC R1a: a partner nearby hears you eat, one further away doesn't (L2, real server, protocol and
///     sound whitelist).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class EatingSoundTest
{
    private const string EAT_SOUND = "event:/player/eat";
    private static readonly NitroxVector3 aliceSpot = new(-146.5f, -12.25f, 56.75f);

    [TestMethod]
    public async Task EatSound_ReachesPartnerWithinTenMeters_NotFurther()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-EatSound");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        await MoveAsync(server, alice, aliceSpot);
        await MoveAsync(server, bob, aliceSpot + new NitroxVector3(6, 0, 0));
        alice.Send(new FMODAssetPacket(EAT_SOUND, aliceSpot, 1f));
        FMODAssetPacket heard = await bob.WaitForAsync<FMODAssetPacket>(packet => packet.AssetPath == EAT_SOUND);
        heard.Position.Should().Be(aliceSpot);
        heard.Volume.Should().BeInRange(0.01f, 1f);

        bob.ClearReceived();
        await MoveAsync(server, bob, aliceSpot + new NitroxVector3(15, 0, 0));
        alice.Send(new FMODAssetPacket(EAT_SOUND, aliceSpot, 1f));
        await bob.AssertNotReceivedAsync<FMODAssetPacket>(packet => packet.AssetPath == EAT_SOUND);
    }

    private static async Task MoveAsync(ServerHarness server, FakePlayer player, NitroxVector3 position)
    {
        player.Send(new PlayerMovement(player.SessionId, position, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName(player.Name, out var p) && p.Position == position,
                                           $"the server has {player.Name}'s position");
    }
}
