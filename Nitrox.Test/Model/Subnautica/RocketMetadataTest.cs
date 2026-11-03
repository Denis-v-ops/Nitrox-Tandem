using System.Collections.Generic;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>MULTINAUGHTICA_SPEC M2: two players' rocket reports are merged, not stored over each other.</summary>
[TestClass]
public class RocketMetadataTest
{
    private static RocketMetadata Rocket(int stage, float transition, int elevatorState, float elevatorPosition, params int[] checks)
    {
        return new RocketMetadata(stage, transition, elevatorState, elevatorPosition, new List<int>(checks));
    }

    [TestMethod]
    public void TheFirstReportIsStoredAsItIs()
    {
        RocketMetadata incoming = Rocket(1, 10f, 3, 0f);

        RocketMetadata.Merge(null, incoming).Should().BeSameAs(incoming);
    }

    [TestMethod]
    public void TwoSwitchesThrownInOneRoundTrip_AreBothKept()
    {
        RocketMetadata merged = RocketMetadata.Merge(Rocket(5, 100f, 3, 0f, 1, 2, 3, 4), Rocket(5, 150f, 3, 0f, 1, 2, 3, 5));

        merged.PreflightChecks.Should().Equal(1, 2, 3, 4, 5);
    }

    [TestMethod]
    public void TheStageNeverGoesDown()
    {
        RocketMetadata.Merge(Rocket(4, 200f, 3, 0f), Rocket(3, 300f, 3, 0f)).CurrentStage.Should().Be(4);
    }

    [TestMethod]
    public void TheTransitionTimeOnlyMovesWithTheStage()
    {
        RocketMetadata.Merge(Rocket(4, 200f, 3, 0f), Rocket(4, 900f, 3, 0f)).LastStageTransitionTime.Should().Be(200f);
        RocketMetadata.Merge(Rocket(4, 200f, 3, 0f), Rocket(5, 900f, 3, 0f)).LastStageTransitionTime.Should().Be(900f);
        RocketMetadata.Merge(Rocket(4, 200f, 3, 0f), Rocket(3, 900f, 3, 0f)).LastStageTransitionTime.Should().Be(200f);
    }

    [TestMethod]
    public void TheElevatorIsWhatTheReporterSees()
    {
        RocketMetadata merged = RocketMetadata.Merge(Rocket(5, 100f, 3, 0f), Rocket(5, 100f, 0, 0.4f));

        merged.ElevatorState.Should().Be(0);
        merged.ElevatorPosition.Should().Be(0.4f);
    }
}
