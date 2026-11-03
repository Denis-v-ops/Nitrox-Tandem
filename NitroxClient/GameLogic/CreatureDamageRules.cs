namespace NitroxClient.GameLogic;

/// <summary>
///     The decisions of <see cref="CreatureHealthManager" /> (MULTINAUGHTICA_SPEC M5), free of Unity types so they can be
///     tested.
/// </summary>
public static class CreatureDamageRules
{
    /// <summary>
    ///     How long after taking a creature over the previous simulating player's last health report is still accepted: its
    ///     report throttle (0.2 s) can hold its last health back until after the lock moved, and that report carries the hits
    ///     the new simulating player doesn't know yet. Ported from MultiNaughtica.
    /// </summary>
    public const float HAND_OVER_GRACE_TIME = 1f;

    public enum HitSource
    {
        /// <summary>
        ///     Damage which the creature's simulating player also gets in its game (environment, the attacks of creatures
        ///     whose every copy attacks, replicated torpedoes...).
        /// </summary>
        World,

        /// <summary>
        ///     Damage computed in this game only (the local player's attacks, what this game simulates): sent to the
        ///     creature's simulating player.
        /// </summary>
        Local,

        /// <summary>
        ///     This game's copy of something another player simulates, whose game sends its own version of the hit.
        /// </summary>
        RemoteCopy
    }

    public enum DamageOutcome
    {
        /// <summary>The hit changes the health here, as in single player.</summary>
        Apply,

        /// <summary>The hit is sent to the creature's simulating player; this game only shows it (effects, sound, flinch).</summary>
        ForwardAndShow,

        /// <summary>The simulating player gets the hit by itself; this game only shows it.</summary>
        Show,

        /// <summary>Nothing happens here.</summary>
        Drop
    }

    /// <summary>
    ///     Who computes a hit whose dealer is a creature (<c>MeleeAttack.OnTouch</c> and its subclasses pass the creature
    ///     itself, game 83031).
    /// </summary>
    /// <param name="attacksFromSimulator">
    ///     <see cref="AI.AttacksPlayersFromSimulator" />: only the creature's simulating game makes it aggressive, so only
    ///     that copy bites (FIX_SPEC S14). False for the four aggression-synced leviathans, whose every copy bites.
    /// </param>
    /// <param name="dealerSimulatedHere">This game holds the attacking creature's lock.</param>
    /// <param name="dealerSimulatedElsewhere">Another game streams the attacking creature to this one.</param>
    public static HitSource CreatureDealerSource(bool attacksFromSimulator, bool dealerSimulatedHere, bool dealerSimulatedElsewhere)
    {
        // Every copy of a leviathan bites: each game's own copy of the prey takes the bite, and its simulator's counts
        if (!attacksFromSimulator)
        {
            return HitSource.World;
        }
        // The prey's simulating game never sees this bite (its copy of the attacker isn't aggressive): it must be sent there,
        // or a predator and a prey simulated by different players never hurt each other
        if (dealerSimulatedHere)
        {
            return HitSource.Local;
        }
        return dealerSimulatedElsewhere ? HitSource.RemoteCopy : HitSource.World;
    }

    /// <summary>
    ///     What a hit on a creature does in this game (not for a hit the server routed here, which always applies).
    /// </summary>
    /// <param name="holdsCreature">This game holds the hit creature's lock: its health is the real one.</param>
    /// <param name="streamedByAnotherPlayer">Another game streams the hit creature's position here (<c>RemotelyControlled</c>).</param>
    /// <param name="canBeHurt">The creature is alive and not invincible.</param>
    /// <param name="positiveDamage">The hit's damage is above 0 (a negative TakeDamage heals).</param>
    public static DamageOutcome DecideDamage(HitSource source, bool holdsCreature, bool streamedByAnotherPlayer, bool canBeHurt, bool positiveDamage)
    {
        if (holdsCreature)
        {
            // The player simulating the dealer computes this hit in its game and sends it here
            return source == HitSource.RemoteCopy ? DamageOutcome.Drop : DamageOutcome.Apply;
        }
        if (source == HitSource.World && !streamedByAnotherPlayer)
        {
            // Nobody streams the creature here, so most likely nobody simulates it (not in SimulationWhitelist): it keeps
            // taking local damage, instead of becoming immune to the environment and other creatures
            return DamageOutcome.Apply;
        }
        if (!canBeHurt)
        {
            return DamageOutcome.Drop;
        }
        return source == HitSource.Local && positiveDamage ? DamageOutcome.ForwardAndShow : DamageOutcome.Show;
    }

    /// <summary>
    ///     Whether this game, which simulates the creature, takes a health report from the previous simulating player.
    ///     Ported from MultiNaughtica (their hand-over grace).
    /// </summary>
    /// <param name="secondsSinceLockTaken">Seconds since this game took the creature over.</param>
    /// <param name="secondsSinceLocalChange">Seconds since the health last changed in this game while it simulated the creature, or null.</param>
    public static bool AcceptsReportAfterHandOver(float secondsSinceLockTaken, float? secondsSinceLocalChange)
    {
        if (secondsSinceLockTaken > HAND_OVER_GRACE_TIME)
        {
            return false;
        }
        // A change made here since the take-over is newer than anything the previous simulating player knew
        return secondsSinceLocalChange == null || secondsSinceLocalChange.Value > secondsSinceLockTaken;
    }
}
