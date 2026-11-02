using System.Threading.Tasks;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC A1: the shared plumbing later co-op items read (L2, real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CoopPlumbingTest
{
    [TestMethod]
    public async Task CoopOptionsReachTheGame_InterferedIsKeptAndRelayed()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CoopPlumbing");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        CoopSettings settings = alice.InitialSync.CoopSettings;
        settings.Should().NotBeNull();
        settings.RescueWindowSeconds.Should().Be(60);
        settings.PartnerSignalInterference.Should().BeTrue();
        settings.SignalInterferenceDepth.Should().Be(300);
        settings.PartnerDiscoveriesSilent.Should().BeTrue();

        bob.Send(new PlayerStats(bob.SessionId, 45, 45, 80, 50, 90, 0.1f, true));
        PlayerStats relayed = await alice.WaitForAsync<PlayerStats>(stats => stats.SessionId == bob.SessionId);
        relayed.Interfered.Should().BeTrue();
        server.Get<PlayerManager>().TryGetPlayerByName("Bob", out ServerPlayer? bobOnServer).Should().BeTrue();
        bobOnServer!.Interfered.Should().BeTrue("the server keeps the latest flag for late joiners");
    }
}
