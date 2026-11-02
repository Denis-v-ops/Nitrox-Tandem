using System.Threading.Tasks;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     Where a returning player is put after a server restart (FIX_SPEC S11, L2: real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class PlayerPositionPersistenceTest
{
    private static readonly NitroxVector3 swimSpot = new(-146.5f, -12.25f, 56.75f);

    /// <summary>
    ///     The last position a player sent is stored, saved, and sent back when they join again after a restart.
    /// </summary>
    [TestMethod]
    public async Task LastMovement_SurvivesSaveAndRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-Position");
        FakePlayer alice = await server.JoinAsync("Alice");

        alice.Send(new PlayerMovement(alice.SessionId, swimSpot, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName("Alice", out var p) && p.Position == swimSpot,
                                           "the server stores Alice's last movement");

        await server.RestartAsync();
        FakePlayer aliceAgain = await server.JoinAsync("Alice");

        aliceAgain.InitialSync.FirstTimeConnecting.Should().BeFalse();
        aliceAgain.InitialSync.PlayerSpawnData.Should().Be(swimSpot);
    }
}
