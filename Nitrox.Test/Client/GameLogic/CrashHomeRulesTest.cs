namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M4 (#2673): what the lock holder of a sulfur plant does with its crash fish
///     (<see cref="CrashHomeRules" />). Ported from MultiNaughtica.
/// </summary>
[TestClass]
public class CrashHomeRulesTest
{
    [TestMethod]
    public void ThePlayerWithoutTheLockNeverMakesAFish()
    {
        CrashHomeRules.DecideSpawn(holdsLock: false, homeHasLivingFish: false).Should().Be(CrashHomeRules.SpawnCall.NotTheHolder);
        CrashHomeRules.DecideSpawn(holdsLock: false, homeHasLivingFish: true).Should().Be(CrashHomeRules.SpawnCall.NotTheHolder);
    }

    [TestMethod]
    public void TheHolderOfAnEmptyHomeMakesTheFish()
    {
        // A home nobody met (the server writes 0) or a home whose timer ran out
        CrashHomeRules.DecideSpawn(holdsLock: true, homeHasLivingFish: false).Should().Be(CrashHomeRules.SpawnCall.MakeFish);
    }

    [TestMethod]
    public void TheHolderOfAHomeThatHasItsFishMakesNoSecondOne()
    {
        // The timer came with a fish: the number is set right instead
        CrashHomeRules.DecideSpawn(holdsLock: true, homeHasLivingFish: true).Should().Be(CrashHomeRules.SpawnCall.FishAlready);
    }

    [TestMethod]
    public void TheHolderWaitsForTheSavedStateOfTheHome()
    {
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: 0f).Should().BeFalse();
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: CrashHomeRules.METADATA_GRACE_SECONDS - 0.1f).Should().BeFalse();
    }

    [TestMethod]
    public void TheHolderActsAsSoonAsTheSavedStateWasApplied()
    {
        CrashHomeRules.MayActOnItsNumber(metadataApplied: true, secondsSinceFirstAsked: 0f).Should().BeTrue();
    }

    [TestMethod]
    public void TheHolderActsAfterTheGraceTimeOfAHomeThatNeverGetsSavedState()
    {
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: CrashHomeRules.METADATA_GRACE_SECONDS).Should().BeTrue();
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: 600f).Should().BeTrue();
    }

    [TestMethod]
    public void ABrokenClockKeepsTheHolderWaiting()
    {
        CrashHomeRules.MayActOnItsNumber(metadataApplied: false, secondsSinceFirstAsked: float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void AFishThatArrivesForAHomeWithoutOneIsAdopted()
    {
        CrashHomeRules.DecideIncomingFish(homeHasLivingFish: false, livingFishIsTheIncomingOne: false).Should().Be(CrashHomeRules.IncomingFishCall.Adopt);
    }

    [TestMethod]
    public void AFishThatArrivesAgainIsAdoptedAndNothingIsDestroyed()
    {
        CrashHomeRules.DecideIncomingFish(homeHasLivingFish: true, livingFishIsTheIncomingOne: true).Should().Be(CrashHomeRules.IncomingFishCall.Adopt);
    }

    [TestMethod]
    public void ASecondFishForAHomeThatHasOneReplacesTheOldOne()
    {
        CrashHomeRules.DecideIncomingFish(homeHasLivingFish: true, livingFishIsTheIncomingOne: false).Should().Be(CrashHomeRules.IncomingFishCall.ReplaceTheOldFish);
    }

    [TestMethod]
    public void AHomeWithAFishIsAtMinusOne()
    {
        // CrashHome.Spawn writes -1 right after it made the fish, and CrashHome.Update only starts the timer when the number is negative (game 83031)
        CrashHomeRules.FISH_ALIVE.Should().Be(-1f);
    }
}
