using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The price of the rescue window (COOP_FEATURES_SPEC T1, §2.2), as pure functions: the window shrinks on repeat
///     downs (full, half, none) until the player rests, and revives give a fixed health.
/// </summary>
internal static class RescueRules
{
    /// <summary>
    ///     The count of downs resets after this many seconds (of <c>TimeService.ActiveTime</c>) without going down: 15 min.
    /// </summary>
    public const double REST_AFTER_SECONDS = 15 * 60;

    /// <summary>
    ///     Health after a revive by hand (a 6 s hold).
    /// </summary>
    public const float HAND_HEALTH = 20f;

    /// <summary>
    ///     Health after a revive with a first aid kit (a 2 s hold): the kit's own heal, <c>SurvivalConstants.kFirstAidKitHealth</c>.
    /// </summary>
    public const float FIRST_AID_KIT_HEALTH = 50f;

    /// <summary>
    ///     The downs counted before this one: 0 if the player never went down or the last time is
    ///     <see cref="REST_AFTER_SECONDS" /> or longer ago.
    /// </summary>
    public static int DownsBefore(int downsSinceRest, double? lastDownedAt, double now)
    {
        if (!lastDownedAt.HasValue || now - lastDownedAt.Value >= REST_AFTER_SECONDS)
        {
            return 0;
        }
        return Math.Max(0, downsSinceRest);
    }

    /// <summary>
    ///     The window in seconds for the <paramref name="down" />-th down since the player last rested (1 = the first).
    ///     With escalation: the full window, then half, then none. 0 means the kill goes through.
    /// </summary>
    public static float WindowSeconds(int optionSeconds, bool escalation, int down)
    {
        if (optionSeconds <= 0 || down < 1)
        {
            return 0;
        }
        if (!escalation)
        {
            return optionSeconds;
        }
        return down switch
        {
            1 => optionSeconds,
            2 => optionSeconds / 2f,
            _ => 0
        };
    }

    public static float HealthFor(PlayerReviveRequest.ReviveMethod method) => method == PlayerReviveRequest.ReviveMethod.FIRST_AID_KIT ? FIRST_AID_KIT_HEALTH : HAND_HEALTH;
}
