namespace NitroxClient.GameLogic.Partner;

/// <summary>
///     Something the local player does to a partner by aiming at their body and pressing or holding the interact button
///     (COOP_FEATURES_SPEC T2: share air; T1: revive; T3: give). Subclasses are registered automatically
///     (<c>ClientAutoFacRegistrar</c>); <see cref="PartnerHandTarget" /> shows the available one with the highest
///     <see cref="Priority" />.
/// </summary>
public abstract class PartnerAction
{
    /// <summary>
    ///     When several actions are available for a partner, the highest one is shown and used.
    /// </summary>
    public virtual int Priority => 0;

    /// <summary>
    ///     Whether the action applies to this partner now (e.g. they are low on air, or down). Checked every frame the
    ///     partner is targeted. An available action can still refuse in <see cref="OnHeld" /> and say why in
    ///     <see cref="GetSubscript" />.
    /// </summary>
    public abstract bool IsAvailable(PartnerHandTarget partner);

    /// <summary>
    ///     The reticle's main line, translated (the interact button is appended).
    /// </summary>
    public abstract string GetText(PartnerHandTarget partner);

    /// <summary>
    ///     The reticle's second line, translated: a hint or why the action can't be done now. Empty for none.
    /// </summary>
    public virtual string GetSubscript(PartnerHandTarget partner) => string.Empty;

    /// <summary>
    ///     Called every frame while the interact button stays held on this partner, starting with the frame after the
    ///     press. <paramref name="heldSeconds" /> includes this frame's <paramref name="deltaTime" />. A hold-to-finish
    ///     action (a revive) acts once <paramref name="heldSeconds" /> reaches its duration; a continuous one (sharing air)
    ///     acts at intervals; a one-shot one (giving) acts on the first call.
    /// </summary>
    /// <param name="progress">Shown on the reticle's progress bar, 0 to 1.</param>
    /// <returns>False to end the hold (done, or not possible); the player must press the button again.</returns>
    public abstract bool OnHeld(PartnerHandTarget partner, float heldSeconds, float deltaTime, out float progress);

    /// <summary>
    ///     True if <paramref name="interval" /> seconds elapsed for the n-th time during this frame of a hold.
    /// </summary>
    protected static bool IntervalElapsed(float heldSeconds, float deltaTime, float interval)
    {
        return (int)(heldSeconds / interval) > (int)((heldSeconds - deltaTime) / interval);
    }
}
