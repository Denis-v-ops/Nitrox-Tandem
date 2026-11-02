using Nitrox.Server.Subnautica.Models.GameLogic;

namespace Nitrox.Test.Server.GameLogic;

/// <summary>
///     FIX_SPEC S12: the server applies the game's infection rules for story goals a player missed while offline.
/// </summary>
[TestClass]
public class InfectionStoryTest
{
    [TestMethod]
    public void Cure_RemovesInfection()
    {
        InfectionStory.Reconcile(1f, ["Infection_Progress4", "Infection_Progress5"]).Should().Be(0f);
    }

    [TestMethod]
    public void Cure_MatchesKeyIgnoringCase()
    {
        InfectionStory.Reconcile(0.75f, ["infection_progress5"]).Should().Be(0f);
    }

    [TestMethod]
    public void Progress_RaisesExistingInfectionToHighestCompletedStep()
    {
        InfectionStory.Reconcile(0.1f, ["Infection_Progress1", "Infection_Progress3"]).Should().Be(0.75f);
    }

    [TestMethod]
    public void Progress_NeverLowersInfection()
    {
        InfectionStory.Reconcile(0.9f, ["Infection_Progress2"]).Should().Be(0.9f);
    }

    [TestMethod]
    public void Progress_DoesNotInfectAnUninfectedPlayer()
    {
        // InfectedMixin.IncreaseInfectedAmount only raises a value above 0
        InfectionStory.Reconcile(0f, ["Infection_Progress4"]).Should().Be(0f);
    }

    [TestMethod]
    public void NoInfectionGoals_KeepsValue()
    {
        InfectionStory.Reconcile(0.1f, ["Goal_Disable_Gun", "OnPlayRadioSunbeam4"]).Should().Be(0.1f);
    }
}
