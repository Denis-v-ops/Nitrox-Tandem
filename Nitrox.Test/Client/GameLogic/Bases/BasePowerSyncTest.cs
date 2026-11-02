using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxClient.GameLogic.Bases;

/// <summary>
///     COOP_FEATURES_SPEC B3 (#1811): what a spawned power source gets, and when the lock holder sends its value.
/// </summary>
[TestClass]
public class BasePowerSyncTest
{
    [TestMethod]
    public void RestoredPower_IsTheStoredValue()
    {
        BasePowerSync.RestoredPower(new PowerSourceMetadata(123.5f), 500f).Should().Be(123.5f);
        BasePowerSync.RestoredPower(new PowerSourceMetadata(0f), 500f).Should().Be(0f, "an empty source stays empty");
        BasePowerSync.RestoredPower(new ThermalPlantMetadata(70f, 42f), 250f).Should().Be(42f);
        BasePowerSync.RestoredPower(new PowerSourceMetadata(900f), 500f).Should().Be(500f, "never more than the source holds");
    }

    [TestMethod]
    public void RestoredPower_IsFull_WhenNothingIsStored()
    {
        // Saves from before B3 (no metadata, or a thermal plant with only its temperature) load as they did: full
        BasePowerSync.RestoredPower(null, 500f).Should().Be(500f);
        BasePowerSync.RestoredPower(new ThermalPlantMetadata(70f, null), 250f).Should().Be(250f);
        BasePowerSync.RestoredPower(new BatteryMetadata(10f), 75f).Should().Be(75f, "other metadata says nothing about power");
    }

    [TestMethod]
    public void ShouldSend_ThrottlesSmallAndFrequentChanges()
    {
        const float MAX = 2500f;
        BasePowerSync.ShouldSend(1000f, 1000f, MAX, 60f).Should().BeFalse("nothing changed");
        BasePowerSync.ShouldSend(1000f, 1010f, MAX, 60f).Should().BeFalse("less than 1 % of the maximum");
        BasePowerSync.ShouldSend(1000f, 1030f, MAX, 2f).Should().BeFalse("the last value is younger than the interval");
        BasePowerSync.ShouldSend(1000f, 1030f, MAX, BasePowerSync.MIN_INTERVAL_SECONDS).Should().BeTrue();
        BasePowerSync.ShouldSend(1000f, 970f, MAX, 6f).Should().BeTrue("draining counts too");
    }

    [TestMethod]
    public void ShouldSend_AtOnce_WhenTheSourceBecomesEmptyOrFull()
    {
        // These turn a base's power (and with it the air inside) on or off
        BasePowerSync.ShouldSend(0.3f, 0f, 75f, 0.5f).Should().BeTrue("ran empty");
        BasePowerSync.ShouldSend(0f, 0.2f, 75f, 0.5f).Should().BeTrue("has power again");
        BasePowerSync.ShouldSend(74.9f, 75f, 75f, 0.5f).Should().BeTrue("became full");
        BasePowerSync.ShouldSend(75f, 74.9f, 75f, 0.5f).Should().BeTrue("no longer full");
    }
}
