using Hit = NitroxClient.GameLogic.PingAimRules.Hit;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M9: where a ping lands. Past the player's own vehicle and, at the helm, the Cyclops; from half a
///     metre on (it was 3 m); onto a partner's body although it is a trigger.
/// </summary>
[TestClass]
public class PingAimRulesTest
{
    private static int Select(bool atHelm, params Hit[] hits) => PingAimRules.SelectHit(hits, atHelm);

    [TestMethod]
    public void TheNearestHitWins()
    {
        Select(false, new Hit(40f), new Hit(12f), new Hit(80f)).Should().Be(1);
    }

    [TestMethod]
    public void NoHit_NoPing()
    {
        Select(false).Should().Be(-1);
        Select(false, new Hit(0.2f), new Hit(5f, isTrigger: true)).Should().Be(-1, "only the player's helmet and a trigger volume");
    }

    [TestMethod]
    public void AnItemAMetreAway_IsPinged()
    {
        // With the old 3 m minimum the ray went on to the rock behind it
        Select(false, new Hit(1f), new Hit(20f)).Should().Be(0);
        Select(false, new Hit(PingAimRules.MIN_DISTANCE), new Hit(20f)).Should().Be(0);
    }

    [TestMethod]
    public void CloserThanHalfAMetre_IsThePlayersOwnGear()
    {
        Select(false, new Hit(0.3f), new Hit(20f)).Should().Be(1);
        Select(false, new Hit(float.NaN), new Hit(20f)).Should().Be(1, "a distance that is no number");
        PingAimRules.MIN_DISTANCE.Should().Be(0.5f);
    }

    [TestMethod]
    public void TheOwnBodyAndTheOwnVehicle_AreSkipped()
    {
        Select(false, new Hit(0.8f, isOwnBody: true), new Hit(20f)).Should().Be(1, "a tool in the hand");
        // The Seamoth's nose or a Prawn suit's arm in front of the seat: the ping reaches what is outside
        Select(false, new Hit(1.5f, isInOwnVehicle: true), new Hit(4f, isInOwnVehicle: true), new Hit(20f)).Should().Be(2);
    }

    [TestMethod]
    public void AtTheHelm_TheCyclopsIsSkipped()
    {
        // The helm console, the floor and the hull below it, then the sea floor
        Hit[] hits = [new Hit(0.9f, isInCurrentSub: true), new Hit(2.5f, isInCurrentSub: true), new Hit(6f, isInCurrentSub: true), new Hit(30f)];
        Select(true, hits).Should().Be(3);
    }

    [TestMethod]
    public void StandingInTheCyclops_ItsWallsArePingable()
    {
        // A passenger, or the pilot who left the helm: "come here" to a spot in the sub
        Select(false, new Hit(6f, isInCurrentSub: true), new Hit(30f)).Should().Be(0);
    }

    [TestMethod]
    public void APartnersBody_IsPingedAlthoughItIsATrigger()
    {
        Select(false, new Hit(10f, isTrigger: true, isPartner: true), new Hit(15f)).Should().Be(0);
        Select(false, new Hit(15f), new Hit(10f, isTrigger: true, isPartner: true)).Should().Be(1, "the order of the hits doesn't matter");
    }

    [TestMethod]
    public void TheComeHereKey_GoesThroughThePartner()
    {
        // "Come here" at the partner would end at once (they are at it): the key pings what is behind them, as before M9
        Hit[] hits = [new Hit(10f, isTrigger: true, isPartner: true), new Hit(10.2f, isPartner: true), new Hit(15f)];
        PingAimRules.SelectHit(hits, false, includePartners: false).Should().Be(2);
        PingAimRules.SelectHit(hits, false).Should().Be(0);
    }

    [TestMethod]
    public void APartnerBehindAWall_IsNot()
    {
        Select(false, new Hit(15f, isTrigger: true, isPartner: true), new Hit(8f)).Should().Be(1);
    }

    [TestMethod]
    public void APartnerInTheCyclopsAtTheHelm_IsPinged()
    {
        Select(true, new Hit(1.5f, isInCurrentSub: true), new Hit(3f, isTrigger: true, isPartner: true, isInCurrentSub: true), new Hit(30f)).Should().Be(1);
    }

    [TestMethod]
    public void OtherTriggers_AreSkipped()
    {
        // Hatches, ladders and other hand targets on the Useable layer are triggers too: the ping lands on the wall behind
        Select(false, new Hit(3f, isTrigger: true), new Hit(9f)).Should().Be(1);
    }
}
