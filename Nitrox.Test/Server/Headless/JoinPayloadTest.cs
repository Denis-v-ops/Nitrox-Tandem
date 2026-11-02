using System.Threading.Tasks;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     What the server sends a joining player (L2, real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class JoinPayloadTest
{
    private static ServerHarness server = null!;

    [ClassInitialize]
    public static async Task StartServer(TestContext context)
    {
        server = await ServerHarness.StartAsync("L2-Join");
    }

    [ClassCleanup]
    public static async Task StopServer()
    {
        await server.DisposeAsync();
    }

    /// <summary>
    ///     S9 (#2899): a new player's escape pod is in their join payload and registered in the world entity manager, so
    ///     later joiners and cell loads know about it.
    /// </summary>
    [TestMethod]
    public async Task NewPlayer_GetsEscapePod_RegisteredInWorldEntityManager()
    {
        FakePlayer alice = await server.JoinAsync("Alice");

        InitialPlayerSync sync = alice.InitialSync;
        sync.FirstTimeConnecting.Should().BeTrue();
        sync.AssignedEscapePodId.Should().NotBeNull();
        sync.GlobalRootEntities.OfType<EscapePodEntity>().Should().ContainSingle(pod => pod.Id == sync.AssignedEscapePodId);
        server.WorldEntityManager.GetGlobalRootEntities<EscapePodEntity>().Should().ContainSingle(pod => pod.Id == sync.AssignedEscapePodId);

        // The partner joining later gets the same pod (one pod holds up to 50 players)
        FakePlayer bob = await server.JoinAsync("Bob");
        bob.InitialSync.AssignedEscapePodId.Should().Be(sync.AssignedEscapePodId);
        bob.InitialSync.GlobalRootEntities.OfType<EscapePodEntity>().Should().ContainSingle(pod => pod.Id == sync.AssignedEscapePodId);
        bob.InitialSync.OtherPlayers.Should().ContainSingle(player => player.PlayerName == "Alice");
        await alice.WaitForAsync<PlayerJoinedMultiplayerSession>(packet => packet.PlayerContext.PlayerName == "Bob");
    }
}
