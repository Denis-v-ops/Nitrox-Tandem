using System.Threading.Tasks;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     FIX_SPEC S12: a player who is offline while the partner takes the cure must not come back infected forever
///     (L2, real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class OfflineInfectionTest
{
    [TestMethod]
    public async Task CureWhileOffline_RejoinsCured()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-Infection");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        // Bob is fully infected when he logs off
        bob.Send(new PlayerStats(bob.SessionId, 45, 45, 80, 50, 90, 1f));
        PlayerManager playerManager = server.Get<PlayerManager>();
        await ServerHarness.WaitUntilAsync(() => playerManager.TryGetPlayerByName("Bob", out var p) && p.Stats?.InfectionAmount == 1f,
                                           "the server stores Bob's infection");
        bob.Dispose();
        await ServerHarness.WaitUntilAsync(() => playerManager.GetConnectedPlayers().All(p => p.Name != "Bob"), "Bob is offline");

        // Alice takes the cure; it runs only in her game
        alice.Send(new StoryGoalExecuted("Infection_Progress5", StoryGoalExecuted.EventType.STORY));
        await ServerHarness.WaitUntilAsync(() => server.Get<StoryManager>().StoryGoalData.CompletedGoals.Contains("Infection_Progress5"),
                                           "the server has the cure goal");

        await server.RestartAsync();
        FakePlayer bobAgain = await server.JoinAsync("Bob");
        bobAgain.InitialSync.PlayerStatsData.InfectionAmount.Should().Be(0f);
    }
}
