using NitroxClient.GameLogic.PlayerLogic;

namespace Nitrox.Test.Client.GameLogic.PlayerLogic;

/// <summary>
///     COOP_FEATURES_SPEC F1: how the partner's marker and name tag look by distance, sight and setting (L1).
/// </summary>
[TestClass]
public class PartnerSignalRulesTest
{
    private const float NAME_TAG_RANGE = 12f;
    private const float PREFAB_MIN_DIST = 15f;
    private const float PREFAB_RANGE = 10f;

    private static PartnerSignalLook Look(PartnerSignalMode mode, float distance, bool inSight = false, float nameTagRange = NAME_TAG_RANGE) =>
        PartnerSignalRules.GetLook(new PartnerSignalContext(mode, distance, inSight, nameTagRange, PREFAB_MIN_DIST, PREFAB_RANGE));

    [TestMethod]
    public void Faint_HiddenUpClose_FaintFurtherOut()
    {
        PartnerSignalLook close = Look(PartnerSignalMode.FAINT, 5);
        close.MinDist.Should().Be(FaintSignalRule.MIN_DIST);
        close.Range.Should().Be(FaintSignalRule.RANGE);
        (close.MinDist > 5).Should().BeTrue("uGUI_Pings hides a marker closer than minDist");
        close.NameTagVisible.Should().BeTrue();

        PartnerSignalLook far = Look(PartnerSignalMode.FAINT, 200);
        far.MarkerHidden.Should().BeFalse();
        far.MinDist.Should().Be(FaintSignalRule.MIN_DIST);
        far.Range.Should().Be(150);
        far.NameTagVisible.Should().BeFalse();
        far.Rule.Should().Be("faint");
    }

    [TestMethod]
    public void Faint_PartnerInSight_NoMarkerUnder25Meters()
    {
        Look(PartnerSignalMode.FAINT, 20, inSight: true).MarkerHidden.Should().BeTrue();
        Look(PartnerSignalMode.FAINT, 20, inSight: false).MarkerHidden.Should().BeFalse("behind a rock the faint marker shows");
        Look(PartnerSignalMode.FAINT, 30, inSight: true).MarkerHidden.Should().BeFalse("beyond 25 m sight doesn't matter");
    }

    [TestMethod]
    public void NameTag_OnlyWithinTheSetRange()
    {
        Look(PartnerSignalMode.FAINT, 12).NameTagVisible.Should().BeTrue();
        Look(PartnerSignalMode.FAINT, 12.5f).NameTagVisible.Should().BeFalse();
        Look(PartnerSignalMode.FAINT, 30, nameTagRange: 40).NameTagVisible.Should().BeTrue();
        Look(PartnerSignalMode.FAINT, 0.5f, nameTagRange: 0).NameTagVisible.Should().BeFalse("0 turns the tag off");
        Look(PartnerSignalMode.OFF, 5).NameTagVisible.Should().BeTrue("Off is about the marker, the tag keeps its own range");
        Look(PartnerSignalMode.ALWAYS, 100).NameTagVisible.Should().BeFalse();
    }

    [TestMethod]
    public void Off_HidesTheMarkerEverywhere_AlwaysKeepsThePrefabDistances()
    {
        foreach (float distance in new[] { 1f, 30f, 500f })
        {
            Look(PartnerSignalMode.OFF, distance).MarkerHidden.Should().BeTrue();
            PartnerSignalLook always = Look(PartnerSignalMode.ALWAYS, distance, inSight: true);
            always.MinDist.Should().Be(PREFAB_MIN_DIST);
            always.Range.Should().Be(PREFAB_RANGE);
        }
    }

    [TestMethod]
    public void RuleSlot_AnEarlierRuleWins()
    {
        IPartnerSignalRule distress = new FixedRule(new PartnerSignalLook("distress", 0, 1, true));
        PartnerSignalRules.Rules.Insert(0, distress);
        try
        {
            Look(PartnerSignalMode.FAINT, 300).Rule.Should().Be("distress");
        }
        finally
        {
            PartnerSignalRules.Rules.Remove(distress);
        }
        Look(PartnerSignalMode.FAINT, 300).Rule.Should().Be("faint");
    }

    private sealed class FixedRule(PartnerSignalLook fixedLook) : IPartnerSignalRule
    {
        public bool TryGetLook(in PartnerSignalContext context, out PartnerSignalLook look)
        {
            look = fixedLook;
            return true;
        }
    }
}
