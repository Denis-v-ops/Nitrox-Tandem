namespace NitroxClient.GameLogic;

/// <summary>
///     What the local player's game knows when a kill reaches the player (COOP_FEATURES_SPEC T1). Plain values, so the
///     decision stays testable without the game.
/// </summary>
/// <param name="FeatureOn">The server's <c>RescueWindowSeconds</c> is above 0.</param>
/// <param name="AlreadyDowned">The player is down (or waiting for the server's answer to going down).</param>
/// <param name="OxygenAvailable">The player's oxygen (<c>Player.GetOxygenAvailable</c>).</param>
/// <param name="SubDestroyed">The player's sub is being destroyed (the Cyclops explosion kills everyone aboard).</param>
public readonly record struct DownedKillContext(bool FeatureOn, bool AlreadyDowned, float OxygenAvailable, bool SubDestroyed, DamageType DamageType);

/// <summary>
///     The rescue window's rules on the game side (COOP_FEATURES_SPEC T1). The window's length is the server's
///     (<c>RescueService</c>); the game only decides which kills go through at once.
/// </summary>
public static class DownedRules
{
    /// <summary>
    ///     Hold time for a revive by hand.
    /// </summary>
    public const float HAND_REVIVE_SECONDS = 6f;

    /// <summary>
    ///     Hold time for a revive when the rescuer carries a first aid kit.
    /// </summary>
    public const float KIT_REVIVE_SECONDS = 2f;

    /// <summary>
    ///     A player who went down and hears nothing from the server for this long dies (the server would have answered
    ///     within a frame or two).
    /// </summary>
    public const float NO_ANSWER_SECONDS = 10f;

    /// <summary>
    ///     A downed player still down this long after their window should have ended dies, in case the server's expiry got
    ///     lost.
    /// </summary>
    public const float EXPIRY_GRACE_SECONDS = 10f;

    /// <summary>
    ///     Less oxygen than this (one second of air in the shallows) counts as out of air: the game starts suffocating at
    ///     exactly 0 (<c>Player.SuffocationUpdate</c>), so a player this low would drown in the window anyway.
    /// </summary>
    public const float OUT_OF_AIR = 1f;

    /// <summary>
    ///     True if this kill puts the player down instead of killing them. Kills go through when the feature is off, the
    ///     player is already down (drowning, the timer, lava while down), out of air, in a sub that is being destroyed, or
    ///     burnt by lava or heat (<see cref="DamageType.Heat" />, what <c>TemperatureDamage</c> deals).
    /// </summary>
    public static bool GoesDown(in DownedKillContext context)
    {
        if (!context.FeatureOn || context.AlreadyDowned || context.SubDestroyed)
        {
            return false;
        }
        if (context.OxygenAvailable < OUT_OF_AIR)
        {
            return false;
        }
        return context.DamageType != DamageType.Heat;
    }

    /// <summary>
    ///     The hold a revive needs.
    /// </summary>
    public static float ReviveSeconds(bool withKit) => withKit ? KIT_REVIVE_SECONDS : HAND_REVIVE_SECONDS;
}
