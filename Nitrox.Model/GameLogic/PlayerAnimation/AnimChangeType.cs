namespace Nitrox.Model.GameLogic.PlayerAnimation;

public enum AnimChangeType
{
    UNDERWATER,
    INFECTION_REVEAL,

    // States of the player's own arms animator, shown on their body in the partner's game (COOP_FEATURES_SPEC R2b)
    USING_TOOL,
    USING_TOOL_ALT,
    USING_BUILDER,
    USING_PDA,
    ON_SURFACE,
    JUMP,
    DIVING,
    DIVING_LAND,
    HOLDING_TOOL
}

public static class AnimChangeTypeExtensions
{
    /// <summary>
    ///     Whether the server keeps the player's latest state of this type and sends it to players who join later
    ///     (COOP_FEATURES_SPEC R2a, R2b). Not kept: the infection reveal (a one-time animation, the game sets a trigger)
    ///     and the fall states <c>jump</c>, <c>diving</c> and <c>diving_land</c>, which are on only while the player is
    ///     in the air.
    /// </summary>
    public static bool IsReplayedToLateJoiners(this AnimChangeType type)
    {
        return type is not (AnimChangeType.INFECTION_REVEAL or AnimChangeType.JUMP or AnimChangeType.DIVING or AnimChangeType.DIVING_LAND);
    }
}
