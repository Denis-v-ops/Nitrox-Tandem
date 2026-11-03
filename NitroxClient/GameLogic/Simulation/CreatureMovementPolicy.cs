namespace NitroxClient.GameLogic.Simulation;

/// <summary>
///     How often the simulating game sends a living creature's pose (FIX_SPEC S16b), so that a partner's copy is where it
///     is and the server's record stays fresh for players who arrive later (S16c). Pure numbers, no Unity calls, so it is
///     unit tested.
/// </summary>
/// <remarks>
///     The watching game carries each pose forward by its velocity (dead reckoning), so the error a partner sees is about
///     how much the creature's velocity changes within one period. Tiers:
///     <list type="bullet">
///         <item>Leviathan class (cell level Far or VeryFar: Reaper, ghosts, Reefback, Sea Dragon; few at a time, fast,
///         seen from far away) when a partner may have it loaded: every <see cref="LEVIATHAN_PERIOD" />.</item>
///         <item>Other creatures within <see cref="CLOSE_DISTANCE" /> of a partner (what they look at, and what bites them):
///         every <see cref="CLOSE_PERIOD" />.</item>
///         <item>Other creatures a partner may have loaded: every <see cref="NEAR_PERIOD" />.</item>
///         <item>Nobody else has it loaded, or it rests: every <see cref="FAR_PERIOD" /> if it moved, which keeps the
///         server's record and cell (and with it the S16 hand-over) about as fresh as the old "on each new swim target"
///         rule (1–5 s).</item>
///     </list>
/// </remarks>
public static class CreatureMovementPolicy
{
    public const float LEVIATHAN_PERIOD = 0.1f;
    public const float CLOSE_PERIOD = 0.2f;
    public const float NEAR_PERIOD = 0.5f;
    public const float FAR_PERIOD = 2f;

    /// <summary>Metres from the nearest partner (per axis) within which a small creature counts as close.</summary>
    public const float CLOSE_DISTANCE = 30f;

    /// <summary>Below this speed (m/s), and with less movement than <see cref="MIN_MOVEMENT" /> since the last pose, a creature rests.</summary>
    public const float RESTING_SPEED = 0.1f;

    /// <summary>Metres a resting creature must have moved since its last pose to be sent again.</summary>
    public const float MIN_MOVEMENT = 0.05f;

    /// <summary>Cell level from which a creature counts as a leviathan (<c>LargeWorldEntity.CellLevel.Far</c>).</summary>
    public const int LEVIATHAN_CELL_LEVEL = 2;

    /// <summary>
    ///     Per-axis distance (m) from a player within which that player's game may have an entity of this cell level loaded.
    ///     Entities load in a box around the player, about ±64 m for level 0 and doubling per level (±512 m for VeryFar,
    ///     read in game for S16), plus a margin. Levels above 3 (batch, global) use the VeryFar range.
    /// </summary>
    public static float LoadRange(int cellLevel)
    {
        return cellLevel switch
        {
            <= 0 => 100f,
            1 => 180f,
            2 => 340f,
            _ => 620f
        };
    }

    /// <param name="cellLevel"><c>LargeWorldEntity.cellLevel</c> of the creature.</param>
    /// <param name="partnerDistance">Per-axis distance to the nearest other player, <see cref="float.PositiveInfinity" /> if none.</param>
    /// <param name="moving">See <see cref="IsMoving" />.</param>
    public static float SendPeriod(int cellLevel, float partnerDistance, bool moving)
    {
        if (!moving || partnerDistance > LoadRange(cellLevel))
        {
            return FAR_PERIOD;
        }
        if (cellLevel >= LEVIATHAN_CELL_LEVEL)
        {
            return LEVIATHAN_PERIOD;
        }
        return partnerDistance <= CLOSE_DISTANCE ? CLOSE_PERIOD : NEAR_PERIOD;
    }

    /// <summary>
    ///     Moving now, or was moving at the last pose sent (the partner's game still carries it forward with that velocity
    ///     until it hears it stopped).
    /// </summary>
    public static bool IsMoving(float speed, float lastSentSpeed)
    {
        return speed > RESTING_SPEED || lastSentSpeed > RESTING_SPEED;
    }

    /// <param name="sinceLastSend">Seconds since its last pose; <see cref="float.PositiveInfinity" /> if never sent.</param>
    /// <param name="movedSinceLastSend">Metres between its position now and in its last pose.</param>
    public static bool ShouldSend(float sinceLastSend, float period, bool moving, float movedSinceLastSend)
    {
        if (sinceLastSend < period)
        {
            return false;
        }
        return moving || movedSinceLastSend > MIN_MOVEMENT;
    }
}
