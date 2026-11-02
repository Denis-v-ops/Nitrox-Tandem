using System;

namespace NitroxClient.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC D2, "the deep cuts the line": when a player is in signal interference, as pure functions of plain
///     values so they are testable without the game. <see cref="SignalInterference" /> gathers the values in game.
/// </summary>
public static class SignalInterferenceRules
{
    /// <summary>
    ///     A deployed beacon within this many metres relays the player's signal.
    /// </summary>
    public const float BEACON_RELAY_RANGE = 150f;

    /// <summary>
    ///     Biome names (<c>Player.GetBiomeString()</c>) that start with one of these, ignoring case, are in interference:
    ///     the Lost River (<c>LostRiver_*</c>), the lava zones (<c>ILZ*</c>/<c>ilz*</c>, <c>InactiveLavaZone</c>,
    ///     <c>LavaLakes*</c>, <c>LavaFalls</c>, <c>LavaPit</c>, <c>ALZ*</c>) and the Precursor facilities
    ///     (<c>Precursor_*</c>, <c>PrecursorCave_*</c>, <c>PrecursorThermalRoom</c>, <c>precursorCache</c>,
    ///     <c>Prison_*</c>). The names are game data, read in game by the A0 probe; their case varies.
    /// </summary>
    public static readonly string[] BIOME_PREFIXES = ["LostRiver", "ILZ", "InactiveLavaZone", "Lava", "ALZ", "Precursor", "Prison"];

    /// <summary>
    ///     Biome names that contain this, ignoring case, are caves: <c>JellyshroomCaves</c>, <c>mushroomForest_cave_light</c>,
    ///     every <c>*_Cave*</c> name, including the <c>*_CaveEntrance</c> ones.
    /// </summary>
    public const string CAVE = "cave";

    public static bool IsInterferenceBiome(string biome)
    {
        if (string.IsNullOrEmpty(biome))
        {
            // Player.GetBiomeString() is empty until the player's first LateUpdate
            return false;
        }
        foreach (string prefix in BIOME_PREFIXES)
        {
            if (biome.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return biome.IndexOf(CAVE, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    ///     Deeper than the server option <c>SignalInterferenceDepth</c> (strictly).
    /// </summary>
    public static bool IsTooDeep(float depth, int interferenceDepth) => depth > interferenceDepth;

    /// <summary>
    ///     In interference: too deep or in an interference biome, and no relay covers the player.
    /// </summary>
    public static bool IsInterfered(float depth, string biome, int interferenceDepth, bool relayed) => (IsTooDeep(depth, interferenceDepth) || IsInterferenceBiome(biome)) && !relayed;

    public static bool IsRelayedByBeacon(float distance) => distance <= BEACON_RELAY_RANGE;

    /// <summary>
    ///     A powered scanner room relays within its scan range: 300 m plus 50 m per range upgrade, at most 500 m
    ///     (<c>MapRoomFunctionality.GetScanRange()</c>, game:MapRoomFunctionality.cs:272).
    /// </summary>
    public static bool IsRelayedByScannerRoom(float distance, bool powered, float scanRange) => powered && distance <= scanRange;

    /// <summary>
    ///     The line between two players is cut when either of them is in interference: whoever is in the Lost River loses
    ///     their partner too.
    /// </summary>
    public static bool IsLinkCut(bool localInterfered, bool partnerInterfered) => localInterfered || partnerInterfered;
}
