namespace NitroxClient.GameLogic;

/// <summary>
///     What a game does with a Sea Emperor baby when its <c>Start</c> has run (MULTINAUGHTICA_SPEC M2). Plain data, no game
///     types, so it can be unit-tested.
/// </summary>
/// <remarks>
///     Three kinds of baby exist in a game:
///     <list type="bullet">
///         <item>
///             The <b>temporary</b> baby of a hatch that another player simulates: it only plays the egg animation
///             (<c>IncubatorEgg_HatchNow_Patch</c>) and is destroyed at its end.
///         </item>
///         <item>
///             The baby <b>hatched here</b>: the real baby of the player holding the incubator's lock. The egg animation
///             calls <c>SwimToMother</c> at its end. Its <c>Start</c> only runs after that: <c>StartHatchAnimation</c>
///             disables it for the cinematic (<c>SeaEmperorBabyCinematicController.SetCinematicMode</c>), and the end of
///             the animation enables it, moves it to the cell root and sends it to the mother in one call. So at
///             <c>Start</c> it already has its swim target.
///         </item>
///         <item>
///             A baby <b>spawned by the server</b>: what the partner, a late joiner and a restarted world get. Nothing
///             calls <c>SwimToMother</c> for it, and without that call the Emperor never registers it
///             (<c>SeaEmperor.RegisterBaby</c>; the mother interaction starts once as many babies as attach points are
///             registered). The old test for this kind, "the baby has no parent", never held: the entity spawner registers
///             every baby with its cell (<c>DefaultWorldEntitySpawner.SetupObject</c>, <c>LargeWorldEntity.Register</c>)
///             before <c>Start</c> runs, so the baby has the cell root as parent.
///         </item>
///     </list>
/// </remarks>
public static class SeaEmperorBabyRules
{
    /// <summary>How long a baby of the server waits for the Emperor to be loaded (its cell can come after the baby's).</summary>
    public const float EMPEROR_WAIT_SECONDS = 60f;

    public enum BabyOrigin
    {
        /// <summary>The animation baby of a hatch simulated by another player.</summary>
        TEMPORARY,

        /// <summary>A baby that still sits under an incubator egg: the egg animation sends it to the mother.</summary>
        HATCHED_HERE,

        /// <summary>A real baby that came from the server.</summary>
        SPAWNED_BY_SERVER
    }

    public enum StartAction
    {
        /// <summary>Nothing to do: other code takes care of the baby.</summary>
        LEAVE_ALONE,

        /// <summary>Register the baby with the Emperor and send it to its attach point.</summary>
        SWIM_TO_MOTHER,

        /// <summary>The Emperor isn't loaded yet: wait for it (up to <see cref="EMPEROR_WAIT_SECONDS" />), then decide again.</summary>
        WAIT_FOR_EMPEROR
    }

    /// <param name="isTemporary">The name carries the marker of the animation babies.</param>
    /// <param name="underIncubatorEgg">The baby is a child of an <c>IncubatorEgg</c> (under the egg's attach point).</param>
    public static BabyOrigin Classify(bool isTemporary, bool underIncubatorEgg)
    {
        if (isTemporary)
        {
            return BabyOrigin.TEMPORARY;
        }
        return underIncubatorEgg ? BabyOrigin.HATCHED_HERE : BabyOrigin.SPAWNED_BY_SERVER;
    }

    public static StartAction DecideStart(BabyOrigin origin, bool emperorExists, bool hasSwimTarget)
    {
        // A baby that already has a swim target was sent to the mother already: never register a baby twice
        if (origin != BabyOrigin.SPAWNED_BY_SERVER || hasSwimTarget)
        {
            return StartAction.LEAVE_ALONE;
        }
        return emperorExists ? StartAction.SWIM_TO_MOTHER : StartAction.WAIT_FOR_EMPEROR;
    }

    /// <summary>
    ///     The attach point of a baby of the server. The hatching game numbers its babies by egg (0 to 4); a baby of the server
    ///     doesn't know its egg, so it takes the next place after the babies already registered, and starts again at the first
    ///     place once all are taken (the Emperor clears her set when the interaction starts, <c>SeaEmperor.StartBabyInteraction</c>).
    /// </summary>
    public static int NextBabyId(int registeredBabies, int attachPoints)
    {
        if (attachPoints <= 0 || registeredBabies <= 0)
        {
            return 0;
        }
        return registeredBabies % attachPoints;
    }

    public static bool EmperorWaitOver(float waitedSeconds) => waitedSeconds >= EMPEROR_WAIT_SECONDS;
}
