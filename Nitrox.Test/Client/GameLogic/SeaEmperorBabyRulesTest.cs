namespace NitroxClient.GameLogic;

/// <summary>MULTINAUGHTICA_SPEC M2: what a Sea Emperor baby does when its Start has run.</summary>
[TestClass]
public class SeaEmperorBabyRulesTest
{
    [TestMethod]
    public void ABabyOfTheServerUnderTheCellRoot_SwimsToTheMother()
    {
        // The spawner registers every baby of the server with its cell before Start, so it always has a parent; the old
        // rule ("no parent") left it where it was. Its origin no longer depends on having a parent.
        SeaEmperorBabyRules.BabyOrigin origin = SeaEmperorBabyRules.Classify(isTemporary: false, underIncubatorEgg: false);

        origin.Should().Be(SeaEmperorBabyRules.BabyOrigin.SPAWNED_BY_SERVER);
        SeaEmperorBabyRules.DecideStart(origin, emperorExists: true, hasSwimTarget: false).Should().Be(SeaEmperorBabyRules.StartAction.SWIM_TO_MOTHER);
    }

    [TestMethod]
    public void ABabyOfTheServerWaitsForTheEmperor()
    {
        SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.SPAWNED_BY_SERVER, emperorExists: false, hasSwimTarget: false)
                           .Should().Be(SeaEmperorBabyRules.StartAction.WAIT_FOR_EMPEROR);
        SeaEmperorBabyRules.EmperorWaitOver(59f).Should().BeFalse();
        SeaEmperorBabyRules.EmperorWaitOver(SeaEmperorBabyRules.EMPEROR_WAIT_SECONDS).Should().BeTrue();
    }

    [TestMethod]
    public void TemporaryBabiesAndBabiesUnderAnEgg_AreLeftAlone()
    {
        SeaEmperorBabyRules.Classify(isTemporary: true, underIncubatorEgg: true).Should().Be(SeaEmperorBabyRules.BabyOrigin.TEMPORARY);
        SeaEmperorBabyRules.Classify(isTemporary: false, underIncubatorEgg: true).Should().Be(SeaEmperorBabyRules.BabyOrigin.HATCHED_HERE);
        SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.TEMPORARY, true, false).Should().Be(SeaEmperorBabyRules.StartAction.LEAVE_ALONE);
        SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.HATCHED_HERE, true, false).Should().Be(SeaEmperorBabyRules.StartAction.LEAVE_ALONE);
    }

    [TestMethod]
    public void ABabyAlreadySwimming_IsNeverRegisteredTwice()
    {
        // The hatching game's baby: the end of the egg animation sent it to the mother before its Start ran
        SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.SPAWNED_BY_SERVER, emperorExists: true, hasSwimTarget: true)
                           .Should().Be(SeaEmperorBabyRules.StartAction.LEAVE_ALONE);
    }

    [TestMethod]
    public void BabiesOfTheServerTakeTheNextAttachPoint_AndStartOverWhenAllAreTaken()
    {
        SeaEmperorBabyRules.NextBabyId(0, 5).Should().Be(0);
        SeaEmperorBabyRules.NextBabyId(3, 5).Should().Be(3);
        SeaEmperorBabyRules.NextBabyId(5, 5).Should().Be(0);
        SeaEmperorBabyRules.NextBabyId(2, 0).Should().Be(0);
    }
}
