using System.Collections.Generic;
using System.Linq;

namespace NitroxClient.GameLogic;

/// <summary>MULTINAUGHTICA_SPEC M2: the radiation radius of a player who joins after the Aurora exploded.</summary>
[TestClass]
public class RadiationRadiusModelTest
{
    private const int TOTAL_LEAKS = 11;
    private const float START = 10f;
    private const float GROW = 0.1f;
    private const float DISSIPATION = -0.05f;
    private const float MAX = 500f;

    private static RadiationRadiusModel.Result Compute(float now, float explosion, IReadOnlyList<float> fixTimes, float current = START)
    {
        return RadiationRadiusModel.Compute(now, explosion, fixTimes, TOTAL_LEAKS, START, GROW, DISSIPATION, MAX, current);
    }

    private static List<float> Leaks(int fixedCount, float fixTime)
    {
        return [.. Enumerable.Range(0, TOTAL_LEAKS).Select(i => i < fixedCount ? fixTime : -1f)];
    }

    [TestMethod]
    public void SomeLeaksFixed_IsNotAllFixed_AndTheFieldStillGrows()
    {
        // 6 of 11 welded: vanilla grows the radius while any leak leaks, and radiationFixed would skip leaksFixedGoal
        RadiationRadiusModel.Result result = Compute(now: 1100f, explosion: 100f, Leaks(6, 600f));

        result.AllFixed.Should().BeFalse();
        result.ApplyRadius.Should().BeTrue();
        result.Radius.Should().BeApproximately(START + 1000f * GROW, 0.001f);
    }

    [TestMethod]
    public void NoLeakFixed_GrowsFromTheExplosion()
    {
        RadiationRadiusModel.Result result = Compute(now: 300f, explosion: 100f, Leaks(0, 0f));

        result.AllFixed.Should().BeFalse();
        result.Radius.Should().BeApproximately(START + 200f * GROW, 0.001f);
    }

    [TestMethod]
    public void BeforeTheExplosion_NothingChanges()
    {
        RadiationRadiusModel.Result result = Compute(now: 50f, explosion: 100f, Leaks(0, 0f), current: 12f);

        result.ApplyRadius.Should().BeFalse();
        result.Radius.Should().Be(12f);
    }

    [TestMethod]
    public void AllFixed_GrewUntilTheLastFix_AndShrankSince()
    {
        List<float> fixTimes = Leaks(TOTAL_LEAKS, 400f);
        fixTimes[3] = 600f;

        RadiationRadiusModel.Result result = Compute(now: 700f, explosion: 100f, fixTimes);

        result.AllFixed.Should().BeTrue();
        result.Radius.Should().BeApproximately(START + 500f * GROW + 100f * DISSIPATION, 0.001f);
    }

    [TestMethod]
    public void NotEveryLeakKnownYet_IsNotAllFixed()
    {
        RadiationRadiusModel.Result result = Compute(now: 700f, explosion: 100f, [.. Leaks(TOTAL_LEAKS, 400f).Take(10)]);

        result.AllFixed.Should().BeFalse();
    }

    [TestMethod]
    public void TheRadiusStaysWithinItsBounds()
    {
        Compute(now: 1e7f, explosion: 0f, Leaks(0, 0f)).Radius.Should().Be(MAX);
        Compute(now: 1e7f, explosion: 0f, Leaks(TOTAL_LEAKS, 10f)).Radius.Should().Be(0f);
    }
}
