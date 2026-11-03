using NitroxClient.GameLogic.PlayerLogic;

namespace Nitrox.Test.Client.GameLogic.PlayerLogic;

/// <summary>
///     COOP_FEATURES_SPEC R2c: when the tool in the partner's hand shows its effects, and which ghost their builder beams at (L1).
/// </summary>
[TestClass]
public class RemoteToolEffectRulesTest
{
    [TestMethod]
    public void UsingTool_PlaysEachKnownTool()
    {
        foreach (RemoteToolKind kind in new[] { RemoteToolKind.SCANNER, RemoteToolKind.BUILDER, RemoteToolKind.WELDER, RemoteToolKind.LASER_CUTTER })
        {
            RemoteToolEffectRules.ShouldPlay(kind, holdingTool: true, usingTool: true, usingToolAlt: false, inCinematic: false).Should().BeTrue(kind.ToString());
            RemoteToolEffectRules.ShouldPlay(kind, holdingTool: true, usingTool: false, usingToolAlt: false, inCinematic: false).Should().BeFalse(kind.ToString());
        }
    }

    [TestMethod]
    public void SelfScan_PlaysOnlyTheScanner()
    {
        RemoteToolEffectRules.ShouldPlay(RemoteToolKind.SCANNER, true, usingTool: false, usingToolAlt: true, inCinematic: false).Should().BeTrue();
        RemoteToolEffectRules.ShouldPlay(RemoteToolKind.WELDER, true, usingTool: false, usingToolAlt: true, inCinematic: false).Should().BeFalse("using_tool_alt is the scanner's self-scan");
    }

    [TestMethod]
    public void OtherTools_NothingInHand_OrToolPutAway_PlayNothing()
    {
        RemoteToolEffectRules.ShouldPlay(RemoteToolKind.NONE, true, true, true, false).Should().BeFalse("a knife swing or a flare sets using_tool too");
        RemoteToolEffectRules.ShouldPlay(RemoteToolKind.WELDER, holdingTool: false, usingTool: true, usingToolAlt: false, inCinematic: false).Should().BeFalse();
    }

    [TestMethod]
    public void Cinematic_StopsTheEffects()
    {
        // SetArmState keeps the partner's states back during a cinematic, so the animator can still show using_tool from before
        RemoteToolEffectRules.ShouldPlay(RemoteToolKind.SCANNER, true, true, false, inCinematic: true).Should().BeFalse();
        RemoteToolEffectRules.ShouldPlay(RemoteToolKind.BUILDER, true, true, false, inCinematic: true).Should().BeFalse();
    }

    [TestMethod]
    public void BuildTarget_IsTheMostRecentlyProgressedGhostInReach()
    {
        (string, float, float)[] candidates =
        [
            ("old", 9.5f, 5f),
            ("latest", 9.9f, 12f),
            ("farAway", 9.95f, RemoteToolEffectRules.BUILD_TARGET_RANGE + 1),
            ("middle", 9.7f, 3f)
        ];
        RemoteToolEffectRules.TryPickBuildTarget(candidates, 10f, out string? target).Should().BeTrue();
        target.Should().Be("latest");
    }

    [TestMethod]
    public void BuildTarget_NoneWhenNothingWasProgressedLately()
    {
        (string, float, float)[] candidates = [("stale", 10f - RemoteToolEffectRules.BUILD_TARGET_MAX_AGE - 0.1f, 2f)];
        RemoteToolEffectRules.TryPickBuildTarget(candidates, 10f, out string? target).Should().BeFalse();
        target.Should().BeNull();
        RemoteToolEffectRules.TryPickBuildTarget(System.Array.Empty<(string, float, float)>(), 10f, out _).Should().BeFalse();
    }
}
