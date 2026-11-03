namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M1: a Cyclops at 0 HP is dying, not destroyed (the game explodes it 18 s after the killing blow),
///     and only the lock holder ends a countdown that got lost.
/// </summary>
[TestClass]
public class CyclopsDestructionRulesTest
{
    [TestMethod]
    public void AtZeroHealth_BeforeTheExplosion_IsNotDestroyed()
    {
        // The extractor used to send "subDestroyed || health <= 0": the partner blew the Cyclops up at the killing blow
        CyclopsDestructionRules.IsDestroyed(0f, false, false).Should().BeFalse();
    }

    [TestMethod]
    public void OnceTheExplosionStarted_OrTheModelsSwapped_IsDestroyed()
    {
        CyclopsDestructionRules.IsDestroyed(0f, false, true).Should().BeTrue("DestroyCyclops ran, subDestroyed follows 2.5 s later");
        CyclopsDestructionRules.IsDestroyed(0f, true, false).Should().BeTrue();
    }

    [TestMethod]
    public void IsDying_OnlyAtZeroHealthAndNotYetDestroyed()
    {
        CyclopsDestructionRules.IsDying(0f, false).Should().BeTrue();
        CyclopsDestructionRules.IsDying(0f, true).Should().BeFalse();
        CyclopsDestructionRules.IsDying(1f, false).Should().BeFalse();
    }

    [TestMethod]
    public void Watchdog_DestroysOnlyOnTheLockHolder_AndOnlyADyingCyclops()
    {
        CyclopsDestructionRules.ShouldWatchdogDestroy(0f, false, true).Should().BeTrue();
        CyclopsDestructionRules.ShouldWatchdogDestroy(0f, false, false).Should().BeFalse("the other game would explode it a second time");
        CyclopsDestructionRules.ShouldWatchdogDestroy(0f, true, true).Should().BeFalse("it already exploded");
        CyclopsDestructionRules.ShouldWatchdogDestroy(200f, false, true).Should().BeFalse("it was repaired in the meantime");
    }

    [TestMethod]
    public void Watchdog_WaitsForTheGamesCountdownAndTheModelSwap()
    {
        // 18 s game:SubRoot.OnTakeDamage + 2.5 s game:CyclopsDestructionEvent.swapModelsDelay
        CyclopsDestructionRules.WATCHDOG_FIRST_CHECK_SECONDS.Should().BeGreaterThan(18f + 2.5f);
    }
}
