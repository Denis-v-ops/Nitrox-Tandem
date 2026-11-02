namespace Nitrox.Model.Subnautica.Helper;

/// <summary>MULTINAUGHTICA_SPEC M2: the story goal that keeps a Precursor teleporter pair active.</summary>
[TestClass]
public class TeleporterGoalKeyTest
{
    [TestMethod]
    public void AFormattedKeyParsesBackToItsIdentifier()
    {
        TeleporterGoalKey.TryParse(TeleporterGoalKey.Format("cragfieldtp"), out string? identifier).Should().BeTrue();
        identifier.Should().Be("cragfieldtp");
    }

    [TestMethod]
    public void TheCaseOfThePrefixDoesNotMatter()
    {
        // StoryGoalManager.completedGoals ignores case
        TeleporterGoalKey.TryParse("nitrox_teleporteractive:guntp", out string? identifier).Should().BeTrue();
        identifier.Should().Be("guntp");
    }

    [TestMethod]
    [DataRow("Goal_Disable_Gun")]
    [DataRow("PrecursorPrisonAquariumFinalTeleporterActive")]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("Nitrox_TeleporterActive:")]
    public void OtherGoalsAreNotTeleporterPairs(string? key)
    {
        TeleporterGoalKey.TryParse(key, out string? identifier).Should().BeFalse();
        identifier.Should().BeNull();
    }
}
