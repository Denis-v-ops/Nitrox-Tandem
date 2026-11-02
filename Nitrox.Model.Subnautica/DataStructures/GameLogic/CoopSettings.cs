using System;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
///     The co-op server options (see <c>SubnauticaServerOptions</c>) that the game needs, sent with the initial sync.
///     Options only the server reads (story gates, rescue escalation, buddy breathing efficiency) are not in here.
/// </summary>
[Serializable]
public class CoopSettings
{
    /// <summary>Seconds a player stays down before dying. 0 disables the rescue window.</summary>
    public int RescueWindowSeconds;

    /// <summary>If true, each game reports whether its player is in interference (caves, the deep).</summary>
    public bool PartnerSignalInterference;

    /// <summary>Depth in meters below which a player is in interference.</summary>
    public int SignalInterferenceDepth;

    /// <summary>If true, a partner's biome and location PDA lines complete silently.</summary>
    public bool PartnerDiscoveriesSilent;

    /// <summary>If true, crafting stations of a base or Cyclops also use the items in its lockers (MULTINAUGHTICA_SPEC M8).</summary>
    public bool CraftFromLockers;
}
