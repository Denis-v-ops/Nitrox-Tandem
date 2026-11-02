namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
///     The story moments that wait for every player on the roster (COOP_FEATURES_SPEC G0). The server decides per kind what
///     "everyone is there" means (<c>StoryGateRules</c> on the server). Values are sent over the network: only append.
/// </summary>
public enum StoryGateKind
{
    /// <summary>The Sunbeam radio message (<c>RadioSunbeam4</c>) that starts the Sunbeam countdown. A lock only: it plays once everyone is online.</summary>
    SUNBEAM_RADIO,

    /// <summary>The last radiation leak in the Aurora's drive core room (G4).</summary>
    DRIVE_CORE,

    /// <summary>Disabling the Precursor gun at its terminal (G1).</summary>
    DISABLE_GUN,

    /// <summary>Launching the rocket (G2).</summary>
    ROCKET_LAUNCH,

    /// <summary>Meeting the Sea Emperor at the aquarium platform (G3a).</summary>
    SEA_EMPEROR,

    /// <summary>Hatching the Sea Emperor's eggs at the incubator (G3b).</summary>
    HATCHING,

    /// <summary>Taking the cure from an enzyme ball (G3c).</summary>
    CURE
}
