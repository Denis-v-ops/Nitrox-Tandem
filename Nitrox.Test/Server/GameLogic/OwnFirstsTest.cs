using System.Collections.Generic;
using Nitrox.Server.Subnautica.Models.GameLogic;

namespace Nitrox.Test.Server.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC D5b: which PDA log entries a joining player doesn't get (L1).
/// </summary>
[TestClass]
public class OwnFirstsTest
{
    private static readonly HashSet<string> aliceHeard = ["Goal_BiomeKelpForest", "Goal_LocationAuroraEntry"];
    private static readonly HashSet<string> bobHeard = ["Goal_LocationAuroraEntry", "Goal_BiomeGrassyPlateaus"];

    [TestMethod]
    public void JoinerDoesntGetWhatOnlyThePartnerHeard()
    {
        OwnFirsts.UnheardBy(bobHeard, [aliceHeard, bobHeard], true).Should().BeEquivalentTo("Goal_BiomeKelpForest");
        OwnFirsts.UnheardBy(aliceHeard, [aliceHeard, bobHeard], true).Should().BeEquivalentTo("Goal_BiomeGrassyPlateaus");
    }

    [TestMethod]
    public void NewPlayerDoesntGetAnyHeardLine()
    {
        OwnFirsts.UnheardBy(new HashSet<string>(), [aliceHeard, bobHeard], true)
                 .Should().BeEquivalentTo("Goal_BiomeKelpForest", "Goal_LocationAuroraEntry", "Goal_BiomeGrassyPlateaus");
    }

    [TestMethod]
    public void OptionOffKeepsTheWholeLog()
    {
        OwnFirsts.UnheardBy(bobHeard, [aliceHeard, bobHeard], false).Should().BeEmpty();
    }
}
