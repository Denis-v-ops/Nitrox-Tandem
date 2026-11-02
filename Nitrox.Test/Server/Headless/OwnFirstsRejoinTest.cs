using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC D5b: each player's heard biome and location lines are kept on the server, so a rejoin doesn't
///     hand a player the partner's lines as already heard; found logs are flagged for the partner (L2, real server and
///     protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class OwnFirstsRejoinTest
{
    private const string KELP_FOREST = "Goal_BiomeKelpForest";
    private const string STORY_LOG = "Story_AuroraWarning1";

    [TestMethod]
    public async Task PartnersLineIsLeftOutOfTheRejoinUntilHeard()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-OwnFirsts");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        // Alice's own game executes the Kelp Forest line (flagged as a biome line) and a story PDA line (not flagged).
        alice.Send(new StoryGoalExecuted(KELP_FOREST, StoryGoalExecuted.EventType.PDA, 100f, experiential: true));
        alice.Send(new StoryGoalExecuted(STORY_LOG, StoryGoalExecuted.EventType.PDA, 101f));
        StoryGoalExecuted relayed = await bob.WaitForAsync<StoryGoalExecuted>(p => p.Key == KELP_FOREST);
        relayed.Experiential.Should().BeTrue();
        await bob.WaitForAsync<StoryGoalExecuted>(p => p.Key == STORY_LOG);

        PlayerManager playerManager = server.Get<PlayerManager>();
        Heard(playerManager, "Alice").Should().BeEquivalentTo(KELP_FOREST);
        Heard(playerManager, "Bob").Should().BeEmpty("a relayed line isn't heard: Bob's game completes it silently");

        await server.RestartAsync();
        FakePlayer bobAgain = await server.JoinAsync("Bob");
        FakePlayer aliceAgain = await server.JoinAsync("Alice");
        Heard(server.Get<PlayerManager>(), "Alice").Should().BeEquivalentTo([KELP_FOREST], "the heard set is saved");
        LogKeys(bobAgain).Should().Contain(STORY_LOG).And.NotContain(KELP_FOREST, "Bob hasn't heard it; his own visit must play it");
        LogKeys(aliceAgain).Should().Contain(STORY_LOG).And.Contain(KELP_FOREST);
        bobAgain.InitialSync.StoryGoalData.CompletedGoals.Should().Contain(KELP_FOREST, "the goal stays complete for both");

        // Bob gets there himself; after that his rejoin has the entry.
        bobAgain.Send(new StoryGoalExecuted(KELP_FOREST, StoryGoalExecuted.EventType.PDA, 500f, experiential: true));
        await aliceAgain.WaitForAsync<StoryGoalExecuted>(p => p.Key == KELP_FOREST);
        bobAgain.Dispose();
        await ServerHarness.WaitUntilAsync(() => server.Get<PlayerManager>().GetConnectedPlayers().All(p => p.Name != "Bob"), "Bob is offline");
        FakePlayer bobThird = await server.JoinAsync("Bob");
        LogKeys(bobThird).Should().Contain(KELP_FOREST);
    }

    [TestMethod]
    public async Task OptionOff_RejoinGetsTheWholeLog()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-OwnFirstsOff");
        server.Get<IOptions<SubnauticaServerOptions>>().Value.PartnerDiscoveriesSilent = false;
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new StoryGoalExecuted(KELP_FOREST, StoryGoalExecuted.EventType.PDA, 100f, experiential: true));
        await bob.WaitForAsync<StoryGoalExecuted>(p => p.Key == KELP_FOREST);
        bob.Dispose();
        await ServerHarness.WaitUntilAsync(() => server.Get<PlayerManager>().GetConnectedPlayers().All(p => p.Name != "Bob"), "Bob is offline");

        FakePlayer bobAgain = await server.JoinAsync("Bob");
        LogKeys(bobAgain).Should().Contain(KELP_FOREST);
    }

    [TestMethod]
    public async Task FoundLogIsFlaggedForThePartner_AndIsShared()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-FoundLog");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        // What Alice's game sends when she picks up the survivor PDA at Lifepod 3 (an Encyclopedia goal): the goal from
        // StoryGoal_Execute_Patch, then the databank entry from PDAEncyclopedia_Add_Patch. And a found PDA-type log.
        alice.Send(new StoryGoalExecuted("Lifepod3", StoryGoalExecuted.EventType.ENCYCLOPEDIA, found: true));
        alice.Send(new PDAEncyclopediaEntryAdd("Lifepod3", true));
        alice.Send(new StoryGoalExecuted("FoundPdaLog", StoryGoalExecuted.EventType.PDA, 100f, found: true));
        StoryGoalExecuted relayed = await bob.WaitForAsync<StoryGoalExecuted>(p => p.Key == "Lifepod3");
        relayed.Found.Should().BeTrue();
        relayed.Experiential.Should().BeFalse();
        (await bob.WaitForAsync<StoryGoalExecuted>(p => p.Key == "FoundPdaLog")).Found.Should().BeTrue();
        Heard(server.Get<PlayerManager>(), "Alice").Should().BeEmpty("a found log isn't a biome or location line");

        bob.Dispose();
        await ServerHarness.WaitUntilAsync(() => server.Get<PlayerManager>().GetConnectedPlayers().All(p => p.Name != "Bob"), "Bob is offline");
        FakePlayer bobAgain = await server.JoinAsync("Bob");
        bobAgain.InitialSync.PDAData.EncyclopediaEntries.Should().Contain("Lifepod3", "found logs wait in everyone's databank");
        LogKeys(bobAgain).Should().Contain("FoundPdaLog", "and in everyone's PDA log");
    }

    private static IEnumerable<string> Heard(PlayerManager playerManager, string name)
    {
        playerManager.TryGetPlayerByName(name, out ServerPlayer? player).Should().BeTrue();
        return player!.HeardGoals.ToList();
    }

    private static List<string> LogKeys(FakePlayer player) => player.InitialSync.PDAData.PDALogEntries.Select(entry => entry.Key).ToList();
}
