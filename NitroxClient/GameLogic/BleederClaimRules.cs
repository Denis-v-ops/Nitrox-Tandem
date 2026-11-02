namespace NitroxClient.GameLogic;

/// <summary>
///     The decisions of the Bleeder claim (<see cref="BleederClaims" />, MULTINAUGHTICA_SPEC M4, FIX_SPEC S15d), free of
///     Unity types so they can be tested.
/// </summary>
public static class BleederClaimRules
{
    /// <summary>
    ///     Seconds after which a claim whose answer never came is asked for again (a lost answer must not block the Bleeder
    ///     for good).
    /// </summary>
    public const float CLAIM_ANSWER_TIMEOUT = 10f;

    public enum ClaimOutcome
    {
        /// <summary>The server refused (another player holds the Bleeder exclusively: it is on them). Undo the contact this game accepted.</summary>
        Detach,
        /// <summary>The lock is ours, but the Bleeder let go or died while the answer was on its way: give the lock back.</summary>
        ReleaseLock,
        /// <summary>The lock is ours and the Bleeder is still on its target: this game runs the attack from now on.</summary>
        KeepLock
    }

    /// <param name="holdsExclusiveLock">This game already holds the Bleeder exclusively, there is nothing to ask for.</param>
    /// <param name="secondsSinceRequest">Seconds since an unanswered request, or null if none is waiting.</param>
    public static bool ShouldRequestClaim(bool holdsExclusiveLock, float? secondsSinceRequest)
    {
        if (holdsExclusiveLock)
        {
            return false;
        }
        return secondsSinceRequest == null || secondsSinceRequest.Value >= CLAIM_ANSWER_TIMEOUT;
    }

    public static ClaimOutcome JudgeClaimResponse(bool lockAcquired, bool stillHasTarget)
    {
        if (!lockAcquired)
        {
            return ClaimOutcome.Detach;
        }
        return stillHasTarget ? ClaimOutcome.KeepLock : ClaimOutcome.ReleaseLock;
    }

    /// <summary>
    ///     Whether the exclusive lock goes back to the normal (transient) hand-over after the Bleeder let go. Not for a
    ///     Bleeder that is leaving the world: <c>AttachAndSuck.OnDisable</c> detaches it, and a dead Bleeder's lock goes with
    ///     its entity.
    /// </summary>
    public static bool ShouldReleaseLockAfterDetach(bool holdsExclusiveLock, bool componentActive, bool bleederAlive)
    {
        return holdsExclusiveLock && componentActive && bleederAlive;
    }

    /// <summary>
    ///     Whether the contact is undone a few seconds after the lock was granted: this game holds the Bleeder and it has
    ///     a target, but the attack never started (the game picks one action per check in turn, and a stronger one can keep
    ///     the attack from running), so the lock would stay exclusive and the player's attach point occupied for good.
    /// </summary>
    public static bool ShouldAbandonClaim(bool holdsExclusiveLock, bool hasTarget, bool attackActive)
    {
        return holdsExclusiveLock && hasTarget && !attackActive;
    }

    /// <summary>
    ///     Whether the Bleeder still shows an attack after the server took its lock from this game. The game ends the attack
    ///     in <c>AttachAndSuck.StopPerform</c>, which doesn't run for a creature this game doesn't simulate, so it is ended
    ///     by hand, or the Bleeder stays pinned to a player who no longer simulates it.
    /// </summary>
    public static bool NeedsDetachAfterLockLoss(bool attackActive, bool hasTarget)
    {
        return attackActive || hasTarget;
    }
}
