namespace Nitrox.Model.GameLogic.FMOD;

[TestClass]
public class FMODWhitelistTest
{
    /// <summary>
    ///     COOP_FEATURES_SPEC R1a: eating and drinking only make a sound (game: Survival.Eat plays TechData's soundUse,
    ///     default event:/player/eat), so those sounds reach a partner nearby.
    /// </summary>
    [TestMethod]
    [DataRow("event:/player/eat")]
    [DataRow("event:/player/drink")]
    [DataRow("event:/player/drink_stillsuit")]
    public void EatingAndDrinkingSounds_AreSyncedWithinTenMeters(string eventPath)
    {
        using FMODWhitelist whitelist = FMODWhitelist.Load(GameInfo.Subnautica);

        whitelist.TryGetSoundData(eventPath, out SoundData soundData).Should().BeTrue();
        soundData.IsWhitelisted.Should().BeTrue();
        soundData.IsGlobal.Should().BeFalse("a partner in another base or vehicle shouldn't hear you eat");
        soundData.Radius.Should().Be(10f);
    }
}
