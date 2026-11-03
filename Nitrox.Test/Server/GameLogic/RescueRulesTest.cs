using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;

namespace Nitrox.Test.Server.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC T1, §2.2: the price of the rescue window.
/// </summary>
[TestClass]
public class RescueRulesTest
{
    [TestMethod]
    public void Window_ShrinksOnRepeatDowns_WithEscalation()
    {
        RescueRules.WindowSeconds(60, true, 1).Should().Be(60);
        RescueRules.WindowSeconds(60, true, 2).Should().Be(30);
        RescueRules.WindowSeconds(60, true, 3).Should().Be(0);
        RescueRules.WindowSeconds(60, true, 7).Should().Be(0);
        RescueRules.WindowSeconds(45, true, 2).Should().Be(22.5f);
    }

    [TestMethod]
    public void Window_StaysFull_WithoutEscalation_AndIsNone_WhenTheOptionIsOff()
    {
        RescueRules.WindowSeconds(60, false, 1).Should().Be(60);
        RescueRules.WindowSeconds(60, false, 5).Should().Be(60);
        RescueRules.WindowSeconds(0, false, 1).Should().Be(0);
        RescueRules.WindowSeconds(0, true, 1).Should().Be(0);
        RescueRules.WindowSeconds(-5, true, 1).Should().Be(0);
    }

    [TestMethod]
    public void DownsBefore_ResetsAfter15MinutesWithoutADown()
    {
        RescueRules.DownsBefore(0, null, 1000).Should().Be(0, "never down");
        RescueRules.DownsBefore(2, 1000, 1000 + 60).Should().Be(2);
        RescueRules.DownsBefore(2, 1000, 1000 + RescueRules.REST_AFTER_SECONDS - 1).Should().Be(2);
        RescueRules.DownsBefore(2, 1000, 1000 + RescueRules.REST_AFTER_SECONDS).Should().Be(0);
        RescueRules.DownsBefore(-3, 1000, 1001).Should().Be(0);
    }

    [TestMethod]
    public void Revive_GivesTheHealthOfItsMethod()
    {
        RescueRules.HealthFor(PlayerReviveRequest.ReviveMethod.HAND).Should().Be(20);
        RescueRules.HealthFor(PlayerReviveRequest.ReviveMethod.FIRST_AID_KIT).Should().Be(50, "a first aid kit heals 50 (SurvivalConstants.kFirstAidKitHealth)");
    }
}
