using System.Collections.Generic;

namespace NitroxClient.GameLogic.PlayerLogic;

/// <summary>
///     The client setting for the partner's marker (COOP_FEATURES_SPEC F1). The values are the setting's list indexes.
/// </summary>
public enum PartnerSignalMode
{
    OFF = 0,
    FAINT = 1,
    ALWAYS = 2
}

/// <summary>
///     What a rule knows about one partner, measured from the local camera. Plain values, so rules stay testable without
///     the game.
/// </summary>
public readonly record struct PartnerSignalContext(
    PartnerSignalMode Mode,
    float Distance,
    bool InLineOfSight,
    float NameTagRange,
    float DefaultMinDist,
    float DefaultRange);

/// <summary>
///     How the partner's marker and name tag look. Only fields of the <see cref="PingInstance" /> are changed, never its
///     visibility or colour: those calls are sent to the server as the player's ping preference.
/// </summary>
public readonly record struct PartnerSignalLook(string Rule, float MinDist, float Range, bool NameTagVisible)
{
    /// <summary>
    ///     A <c>minDist</c> no camera gets beyond: <c>uGUI_Pings</c> then hides the marker.
    /// </summary>
    public const float HIDDEN = float.MaxValue;

    public bool MarkerHidden => MinDist >= HIDDEN;
}

/// <summary>
///     One way the partner's marker can look. The first rule in <see cref="PartnerSignalRules.Rules" /> that applies wins.
/// </summary>
public interface IPartnerSignalRule
{
    bool TryGetLook(in PartnerSignalContext context, out PartnerSignalLook look);
}

public static class PartnerSignalRules
{
    /// <summary>
    ///     Checked in order. "Faint" applies always, so it stays last: COOP_FEATURES_SPEC D2 ("last contact") and D3
    ///     ("distress") go in front of it.
    /// </summary>
    public static readonly List<IPartnerSignalRule> Rules = [new FaintSignalRule()];

    public static PartnerSignalLook GetLook(in PartnerSignalContext context)
    {
        foreach (IPartnerSignalRule rule in Rules)
        {
            if (rule.TryGetLook(context, out PartnerSignalLook look))
            {
                return look;
            }
        }
        return FaintSignalRule.Look(context);
    }
}

/// <summary>
///     COOP_FEATURES_SPEC F1: a faint marker at a distance, nothing while the partner is close or in sight, their name tag
///     only up close.
/// </summary>
public sealed class FaintSignalRule : IPartnerSignalRule
{
    /// <summary>
    ///     Closer than this the marker is hidden; the partner's body and name tag take over.
    /// </summary>
    public const float MIN_DIST = 12f;

    /// <summary>
    ///     Within this distance a partner in plain sight has no marker.
    /// </summary>
    public const float LINE_OF_SIGHT_DIST = 25f;

    /// <summary>
    ///     The marker fades in over this many metres beyond <see cref="MIN_DIST" /> (<c>uGUI_Pings</c> lerps the alpha
    ///     from 0 to 0.5 over <c>range</c>), so it stays faint.
    /// </summary>
    public const float RANGE = 150f;

    public bool TryGetLook(in PartnerSignalContext context, out PartnerSignalLook look)
    {
        look = Look(context);
        return true;
    }

    public static PartnerSignalLook Look(in PartnerSignalContext context)
    {
        bool nameTagVisible = context.Distance <= context.NameTagRange;
        return context.Mode switch
        {
            PartnerSignalMode.OFF => new PartnerSignalLook("off", PartnerSignalLook.HIDDEN, RANGE, nameTagVisible),
            // The marker as it was before F1: the signal prefab's own distances
            PartnerSignalMode.ALWAYS => new PartnerSignalLook("always", context.DefaultMinDist, context.DefaultRange, nameTagVisible),
            _ when context.InLineOfSight && context.Distance < LINE_OF_SIGHT_DIST => new PartnerSignalLook("faint", PartnerSignalLook.HIDDEN, RANGE, nameTagVisible),
            _ => new PartnerSignalLook("faint", MIN_DIST, RANGE, nameTagVisible)
        };
    }
}
