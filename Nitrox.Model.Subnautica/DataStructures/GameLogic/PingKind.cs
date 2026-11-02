namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
///     What a player ping says (COOP_FEATURES_SPEC F2). The receiving game builds the label in its own language.
/// </summary>
public enum PingKind : byte
{
    /// <summary>"Come here": the Beacon icon, until the partner arrives or 30 s.</summary>
    ComeHere,

    /// <summary>"Look": a pickupable, breakable or drillable resource, shown with that item's own icon for 10 s.</summary>
    Look,

    /// <summary>"Danger": an aggressive creature, a red Signal icon for 15 s.</summary>
    Danger
}
