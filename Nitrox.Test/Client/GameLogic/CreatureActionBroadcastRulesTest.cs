using static NitroxClient.GameLogic.CreatureActionBroadcastRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M4: the simulating game sent a creature's action on every AI update (<see cref="AI.BroadcastNewAction" />),
///     and threw on the null action <c>Creature.ChooseBestAction</c> can return. Now: changes only, a refresh every
///     <see cref="REFRESH_INTERVAL" /> seconds, and "no action" as <see cref="NO_ACTION" />.
/// </summary>
[TestClass]
public class CreatureActionBroadcastRulesTest
{
    [TestMethod]
    public void TheFirstActionOfACreatureIsSent()
    {
        ShouldSend(null, null, "AttackLastTarget", 12f).Should().BeTrue();
        ShouldSend(null, null, NO_ACTION, 12f).Should().BeTrue();
    }

    [TestMethod]
    public void AnUnchangedActionIsNotSentAgainBeforeTheRefresh()
    {
        ShouldSend("SwimRandom", 10f, "SwimRandom", 10f).Should().BeFalse("ChooseBestAction runs on every AI update");
        ShouldSend("SwimRandom", 10f, "SwimRandom", 10f + REFRESH_INTERVAL - 0.1f).Should().BeFalse();
        ShouldSend(NO_ACTION, 10f, NO_ACTION, 11f).Should().BeFalse();
    }

    [TestMethod]
    public void AChangedActionIsSentAtOnce()
    {
        ShouldSend("SwimRandom", 10f, "AttackLastTarget", 10.02f).Should().BeTrue();
        ShouldSend("AttackLastTarget", 10f, NO_ACTION, 10.02f).Should().BeTrue("the other games must stop the attack too");
        ShouldSend(NO_ACTION, 10f, "SwimRandom", 10.02f).Should().BeTrue();
    }

    [TestMethod]
    public void AnUnchangedActionIsRefreshed_ForPlayersWhoLoadedTheCreatureSince()
    {
        ShouldSend("SwimRandom", 10f, "SwimRandom", 10f + REFRESH_INTERVAL).Should().BeTrue();
        ShouldSend("SwimRandom", 10f, "SwimRandom", 600f).Should().BeTrue();
    }

    [TestMethod]
    public void ABrokenClockStillRefreshes()
    {
        ShouldSend("SwimRandom", 10f, "SwimRandom", float.NaN).Should().BeTrue();
    }
}
