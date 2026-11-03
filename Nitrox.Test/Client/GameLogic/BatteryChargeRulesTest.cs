namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M1: the battery charge that is sent is the one after the change, and an empty battery is reported.
/// </summary>
[TestClass]
public class BatteryChargeRulesTest
{
    [TestMethod]
    public void AChangeWithinTheSameWholeNumberIsNotSent()
    {
        BatteryChargeRules.ShouldSend(100.9f, 100.2f).Should().BeFalse();
    }

    [TestMethod]
    public void CrossingAWholeNumberIsSent_DrainingOrCharging()
    {
        BatteryChargeRules.ShouldSend(100.1f, 99.95f).Should().BeTrue();
        BatteryChargeRules.ShouldSend(99.95f, 100.05f).Should().BeTrue();
    }

    [TestMethod]
    public void TheStepThatEmptiesTheBatteryIsSent_EvenWithinTheLastWholeNumber()
    {
        // IsDepleted is "charge == 0": a copy that keeps 0.4 looks powered
        BatteryChargeRules.ShouldSend(0.4f, 0f).Should().BeTrue();
        BatteryChargeRules.ShouldSend(0.4f, -0.1f).Should().BeTrue();
    }

    [TestMethod]
    public void AnEmptyBatteryThatStaysEmptyIsNotSentAgain()
    {
        BatteryChargeRules.ShouldSend(0f, 0f).Should().BeFalse();
    }

    [TestMethod]
    public void TheStoredChargeIsLimitedToTheCapacity_NotBelowZero()
    {
        // game:Battery.charge set => _charge = Mathf.Min(value, _capacity)
        BatteryChargeRules.StoredCharge(250f, 200f).Should().Be(200f);
        BatteryChargeRules.StoredCharge(150f, 200f).Should().Be(150f);
        BatteryChargeRules.StoredCharge(-1f, 200f).Should().Be(-1f);
    }

    [TestMethod]
    public void ChargingToTheCapacity_SendsTheCapacity()
    {
        float stored = BatteryChargeRules.StoredCharge(200.3f, 200f);

        BatteryChargeRules.ShouldSend(199.7f, stored).Should().BeTrue();
        stored.Should().Be(200f);
    }
}
