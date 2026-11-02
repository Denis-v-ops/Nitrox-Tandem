namespace NitroxClient.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC D2: when a player is in signal interference (L1). The biome names are the game's own, read in game
///     by the A0 probe (<c>Player.GetBiomeString()</c> and <c>WaterBiomeManager.main.biomeSettings[].name</c>).
/// </summary>
[TestClass]
public class SignalInterferenceRulesTest
{
    private const int DEPTH = 300;

    [TestMethod]
    public void DeeperThanTheOption_IsInterference()
    {
        SignalInterferenceRules.IsInterfered(400, "bloodKelpTwo", DEPTH, relayed: false).Should().BeTrue();
        SignalInterferenceRules.IsInterfered(300.01f, "deepGrandReef", DEPTH, relayed: false).Should().BeTrue();
        SignalInterferenceRules.IsInterfered(300, "deepGrandReef", DEPTH, relayed: false).Should().BeFalse("deeper than, not at");
        SignalInterferenceRules.IsInterfered(50, "GrassyPlateaus", DEPTH, relayed: false).Should().BeFalse();
        SignalInterferenceRules.IsInterfered(400, "bloodKelpTwo", 500, relayed: false).Should().BeFalse("the depth is the server's option");
    }

    [TestMethod]
    // Read in game at known spots (A0 probe)
    [DataRow("JellyshroomCaves")]
    [DataRow("mushroomForest_cave_light")]
    [DataRow("LostRiver_Junction")]
    [DataRow("LostRiver_GhostTree")]
    [DataRow("ILZCastleChamber")]
    [DataRow("Precursor_LavaCastleBase")]
    [DataRow("LavaLakes")]
    [DataRow("Prison_Aquarium_Upper")]
    // From the biome settings list
    [DataRow("jellyshroomCaves")]
    [DataRow("jellyshroomCaves_Entrance")]
    [DataRow("ilzChamber")]
    [DataRow("InactiveLavaZone")]
    [DataRow("LavaFalls")]
    [DataRow("LavaPit")]
    [DataRow("ALZChamber")]
    [DataRow("ALZFalls")]
    [DataRow("precursorCache")]
    [DataRow("PrecursorCave_Cragfield")]
    [DataRow("PrecursorThermalRoom")]
    [DataRow("Precursor_Gun_ControlRoom")]
    [DataRow("safeShallows_Cave")]
    [DataRow("kelpForest_CaveEntrance")]
    [DataRow("kooshZone_cave_dark")]
    [DataRow("Mountains_IslandSurface_Cave")]
    [DataRow("FloatingIslandCaveTeleporter")]
    public void CavesLostRiverLavaAndPrecursorBiomes_AreInterference(string biome)
    {
        SignalInterferenceRules.IsInterferenceBiome(biome).Should().BeTrue();
        SignalInterferenceRules.IsInterfered(20, biome, DEPTH, relayed: false).Should().BeTrue("the biome counts at any depth");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("Lifepod")]
    [DataRow("safeShallows")]
    [DataRow("kelpForest")]
    [DataRow("GrassyPlateaus")]
    [DataRow("CragField")]
    [DataRow("mountains")]
    [DataRow("FloatingIsland")]
    [DataRow("deepGrandReef")]
    [DataRow("bloodKelpTwo")]
    [DataRow("crashedShip")]
    [DataRow("CrashedShip_Interior_Cargo")]
    [DataRow("sparseReef_Deep")]
    [DataRow("underwaterIslands_wreck")]
    public void OpenWaterAndTheAurora_AreNot(string biome)
    {
        SignalInterferenceRules.IsInterferenceBiome(biome).Should().BeFalse();
        SignalInterferenceRules.IsInterfered(250, biome, DEPTH, relayed: false).Should().BeFalse();
    }

    [TestMethod]
    public void ARelayCoversThePlayer()
    {
        SignalInterferenceRules.IsInterfered(400, "bloodKelpTwo", DEPTH, relayed: true).Should().BeFalse();
        SignalInterferenceRules.IsInterfered(100, "LostRiver_Junction", DEPTH, relayed: true).Should().BeFalse();
    }

    [TestMethod]
    public void DeployedBeacon_RelaysWithin150Meters()
    {
        SignalInterferenceRules.IsRelayedByBeacon(3).Should().BeTrue();
        SignalInterferenceRules.IsRelayedByBeacon(150).Should().BeTrue();
        SignalInterferenceRules.IsRelayedByBeacon(150.5f).Should().BeFalse();
    }

    [TestMethod]
    public void ScannerRoom_RelaysWithinItsScanRange_OnlyWhenPowered()
    {
        // MapRoomFunctionality.GetScanRange(): 300 m, plus 50 m per range upgrade, at most 500 m
        SignalInterferenceRules.IsRelayedByScannerRoom(299, powered: true, scanRange: 300).Should().BeTrue();
        SignalInterferenceRules.IsRelayedByScannerRoom(320, powered: true, scanRange: 300).Should().BeFalse();
        SignalInterferenceRules.IsRelayedByScannerRoom(320, powered: true, scanRange: 350).Should().BeTrue("one range upgrade");
        SignalInterferenceRules.IsRelayedByScannerRoom(499, powered: true, scanRange: 500).Should().BeTrue("four upgrades");
        SignalInterferenceRules.IsRelayedByScannerRoom(10, powered: false, scanRange: 500).Should().BeFalse("an unpowered scanner room relays nothing");
    }

    [TestMethod]
    public void TheLinkIsCut_WhenEitherPlayerIsInInterference()
    {
        SignalInterferenceRules.IsLinkCut(false, false).Should().BeFalse();
        SignalInterferenceRules.IsLinkCut(true, false).Should().BeTrue("whoever is in the Lost River loses their partner too");
        SignalInterferenceRules.IsLinkCut(false, true).Should().BeTrue();
        SignalInterferenceRules.IsLinkCut(true, true).Should().BeTrue();
    }
}
