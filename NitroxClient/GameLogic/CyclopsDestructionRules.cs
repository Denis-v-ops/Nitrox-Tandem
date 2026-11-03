namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M1: when a Cyclops counts as destroyed, and when a game destroys a Cyclops whose countdown is lost.
///     Plain data, no game types.
/// </summary>
public static class CyclopsDestructionRules
{
    /// <summary>
    ///     The 18 s of the game's countdown (<c>SubRoot.OnTakeDamage</c>: <c>Invoke("DestroyCyclopsSubRoot", 18f)</c>), the 2.5 s
    ///     before <c>CyclopsDestructionEvent.SwapToDamagedModels</c> sets <c>subDestroyed</c>, and a margin for the packet.
    /// </summary>
    public const float WATCHDOG_FIRST_CHECK_SECONDS = 25f;

    public const float WATCHDOG_CHECK_INTERVAL_SECONDS = 5f;

    /// <summary>
    ///     What <c>CyclopsMetadata.IsDestroyed</c> says. Health 0 doesn't count: the killing blow only starts the game's 18 s
    ///     countdown, and a partner told "destroyed" at that moment blew the Cyclops up at once, killing anyone inside.
    /// </summary>
    /// <param name="health">The Cyclops' health (not used: at 0 it is dying, not destroyed).</param>
    /// <param name="subDestroyed"><c>SubRoot.subDestroyed</c>, set 2.5 s after the explosion.</param>
    /// <param name="explosionStarted"><c>CyclopsDestructionEvent.DestroyCyclops</c> ran and the model swap is pending.</param>
    public static bool IsDestroyed(float health, bool subDestroyed, bool explosionStarted)
    {
        return subDestroyed || explosionStarted;
    }

    /// <summary>
    ///     A Cyclops at 0 HP that hasn't exploded is waiting for a countdown that runs only on the game that took the hit.
    /// </summary>
    public static bool IsDying(float health, bool subDestroyed) => health <= 0f && !subDestroyed;

    /// <summary>
    ///     When the watchdog's wait is over: only the lock holder destroys it, once, so loot, beacon and the update go out once.
    /// </summary>
    public static bool ShouldWatchdogDestroy(float health, bool subDestroyed, bool holdsLock) => IsDying(health, subDestroyed) && holdsLock;
}
