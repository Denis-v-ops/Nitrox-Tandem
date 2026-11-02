using NitroxClient.GameLogic.PlayerLogic;

namespace Nitrox.Test.Client.GameLogic.PlayerLogic;

/// <summary>
///     COOP_FEATURES_SPEC F1: how the partner's marker and name tag look by distance, sight and setting; D2: the last
///     contact while the line is cut; D3: the distress call (L1).
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

    private static PartnerSignalLook CutLook(PartnerSignalMode mode, float distance, float secondsSinceContact = 1, bool inSight = false) =>
        PartnerSignalRules.GetLook(new PartnerSignalContext(mode, distance, inSight, NAME_TAG_RANGE, PREFAB_MIN_DIST, PREFAB_RANGE, LinkCut: true, SecondsSinceContact: secondsSinceContact));

    [TestMethod]
    public void D2_LinkCut_TheFaintMarkerFreezesAtLastContact()
    {
        PartnerSignalLook look = CutLook(PartnerSignalMode.FAINT, 400);
        look.Rule.Should().Be(LastContactRule.NAME);
        look.Frozen.Should().BeTrue();
        look.MinDist.Should().Be(FaintSignalRule.MIN_DIST);
        look.Range.Should().Be(FaintSignalRule.RANGE, "as faint as the faint marker");
        look.NameTagVisible.Should().BeFalse();

        PartnerSignalLook intact = Look(PartnerSignalMode.FAINT, 400);
        intact.Rule.Should().Be("faint");
        intact.Frozen.Should().BeFalse("the marker follows the partner while neither is in interference");
    }

    [TestMethod]
    public void D2_TheFrozenMarkerFlickersSlowly()
    {
        CutLook(PartnerSignalMode.FAINT, 400, secondsSinceContact: 0.5f).MarkerHidden.Should().BeFalse();
        CutLook(PartnerSignalMode.FAINT, 400, secondsSinceContact: 2.4f).MarkerHidden.Should().BeFalse();
        CutLook(PartnerSignalMode.FAINT, 400, secondsSinceContact: 2.6f).MarkerHidden.Should().BeTrue("gone for half a second every 3 s");
        CutLook(PartnerSignalMode.FAINT, 400, secondsSinceContact: 3.1f).MarkerHidden.Should().BeFalse();
        CutLook(PartnerSignalMode.FAINT, 400, secondsSinceContact: 125.7f).MarkerHidden.Should().BeTrue();
        CutLook(PartnerSignalMode.FAINT, 400, secondsSinceContact: 2.6f).Frozen.Should().BeTrue("still the last-contact rule while dark");
    }

    [TestMethod]
    public void D2_UpCloseNothingChanges()
    {
        foreach ((float distance, bool inSight) in new[] { (5f, false), (11f, false), (20f, true) })
        {
            CutLook(PartnerSignalMode.FAINT, distance, inSight: inSight).Should().Be(Look(PartnerSignalMode.FAINT, distance, inSight), $"at {distance} m, in sight {inSight}");
        }
        CutLook(PartnerSignalMode.FAINT, 5).NameTagVisible.Should().BeTrue("the name tag keeps its range");
        CutLook(PartnerSignalMode.FAINT, 20, inSight: false).Frozen.Should().BeTrue("out of sight at 20 m the partner is out of contact");
    }

    [TestMethod]
    public void D2_AlwaysIgnoresInterference_OffStaysOff()
    {
        CutLook(PartnerSignalMode.ALWAYS, 400).Should().Be(Look(PartnerSignalMode.ALWAYS, 400));
        CutLook(PartnerSignalMode.OFF, 400).MarkerHidden.Should().BeTrue();
        CutLook(PartnerSignalMode.OFF, 400).Frozen.Should().BeFalse();
    }

    [TestMethod]
    public void D2_LastContactRuleComesBeforeFaint_D3DistressBeforeBoth()
    {
        PartnerSignalRules.Rules[0].Should().BeOfType<DistressRule>();
        PartnerSignalRules.Rules[1].Should().BeOfType<LastContactRule>();
        PartnerSignalRules.Rules[^1].Should().BeOfType<FaintSignalRule>();
    }

    private static PartnerSignalLook DistressLook(PartnerSignalMode mode, float distance, bool linkCut = false, bool inSight = false) =>
        PartnerSignalRules.GetLook(new PartnerSignalContext(mode, distance, inSight, NAME_TAG_RANGE, PREFAB_MIN_DIST, PREFAB_RANGE, LinkCut: linkCut, SecondsSinceContact: 2.6f, Distress: true));

    [TestMethod]
    public void D3_Distress_FullAlphaAtAnyDistance_InEveryMode_ThroughInterference()
    {
        foreach (PartnerSignalMode mode in new[] { PartnerSignalMode.FAINT, PartnerSignalMode.ALWAYS, PartnerSignalMode.OFF })
        {
            foreach (float distance in new[] { 20f, 150f, 800f })
            {
                foreach (bool linkCut in new[] { false, true })
                {
                    PartnerSignalLook look = DistressLook(mode, distance, linkCut, inSight: distance < 25);
                    look.Rule.Should().Be(DistressRule.NAME, $"{mode} at {distance} m, link cut {linkCut}");
                    look.MarkerHidden.Should().BeFalse("not even the D2 flicker hides it (2.6 s since contact)");
                    look.MinDist.Should().Be(DistressRule.MIN_DIST);
                    // uGUI_Pings: alpha = Lerp(0, 0.5, (distance - minDist) / range), at its maximum from minDist + range on
                    ((distance - look.MinDist) / look.Range).Should().BeGreaterThanOrEqualTo(1);
                    look.Frozen.Should().BeTrue("it is placed by the pulses, not a live track");
                }
            }
        }
        DistressLook(PartnerSignalMode.FAINT, 2).NameTagVisible.Should().BeTrue();
        DistressLook(PartnerSignalMode.FAINT, 2).MinDist.Should().BeGreaterThan(2, "right at the body the marker hides");
    }

    [TestMethod]
    public void D3_NoDistress_TheUsualRules()
    {
        PartnerSignalRules.GetLook(new PartnerSignalContext(PartnerSignalMode.FAINT, 400, false, NAME_TAG_RANGE, PREFAB_MIN_DIST, PREFAB_RANGE, LinkCut: true, Distress: false))
                          .Rule.Should().Be(LastContactRule.NAME);
        Look(PartnerSignalMode.FAINT, 400).Rule.Should().Be("faint");
    }

    [TestMethod]
    public void D3_PulsesEveryFiveSeconds_LabelCountsDown()
    {
        DistressRule.IsPulseDue(null, 100).Should().BeTrue("the first pulse comes at once");
        DistressRule.IsPulseDue(100, 104.9f).Should().BeFalse();
        DistressRule.IsPulseDue(100, 105).Should().BeTrue();
        DistressRule.IsPulseDue(100, 50).Should().BeTrue("a clock that went back doesn't stop the pulses");
        DistressRule.SecondsLabel(42.1f).Should().Be(43);
        DistressRule.SecondsLabel(0.2f).Should().Be(1);
        DistressRule.SecondsLabel(0).Should().Be(0);
        DistressRule.SecondsLabel(-3).Should().Be(0);
    }

    [TestMethod]
    public void D2_LabelAge_SecondsThenMinutes()
    {
        LastContactRule.Age(0).Should().Be((0, false));
        LastContactRule.Age(42.9f).Should().Be((42, false));
        LastContactRule.Age(59.99f).Should().Be((59, false));
        LastContactRule.Age(60).Should().Be((1, true));
        LastContactRule.Age(150).Should().Be((2, true));
        LastContactRule.Age(-1).Should().Be((0, false));
    }

    [TestMethod]
    public void TheMarkerIsThePartnersSignal_NotACarriedBeacon()
    {
        string markerName = PartnerSignalRules.MarkerObjectName("Bob");
        markerName.Should().Be("signal_Bob");
        PartnerSignalRules.IsPartnerMarker(markerName, "Bob").Should().BeTrue();
        // What else under a partner's body has a PingInstance: a beacon in their inventory
        PartnerSignalRules.IsPartnerMarker("Beacon(Clone)", "Bob").Should().BeFalse();
        PartnerSignalRules.IsPartnerMarker("Beacon", "Bob").Should().BeFalse();
        PartnerSignalRules.IsPartnerMarker("signal_Alice", "Bob").Should().BeFalse();
        PartnerSignalRules.IsPartnerMarker("signal_Bob(Clone)", "Bob").Should().BeFalse("AttachPing sets the exact name");
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
