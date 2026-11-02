using System.Threading.Tasks;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC D1: a player's pain sound reaches the partner, not the player themselves (L2). The partner's
///     game decides by distance whether it's heard.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class PainSoundRelayTest
{
    [TestMethod]
    public async Task PainSound_ReachesThePartner()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-PainSound");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new PlayerPainSound(alice.SessionId, PlayerPainSound.PainKind.WITHOUT_TANK_LIGHT));
        PlayerPainSound heard = await bob.WaitForAsync<PlayerPainSound>();
        heard.SessionId.Should().Be(alice.SessionId);
        heard.Kind.Should().Be(PlayerPainSound.PainKind.WITHOUT_TANK_LIGHT);
        await alice.AssertNotReceivedAsync<PlayerPainSound>();
    }
}
