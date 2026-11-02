using System;
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
///     What a rule knows about one partner, measured from the local camera to the partner's body. Plain values, so rules
///     stay testable without the game.
/// </summary>
/// <param name="LinkCut">Either player is in signal interference (COOP_FEATURES_SPEC D2).</param>
/// <param name="SecondsSinceContact">While the link is cut: seconds since it was cut.</param>
public readonly record struct PartnerSignalContext(
    PartnerSignalMode Mode,
    float Distance,
    bool InLineOfSight,
    float NameTagRange,
    float DefaultMinDist,
    float DefaultRange,
    bool LinkCut = false,
    float SecondsSinceContact = 0);

/// <summary>
///     How the partner's marker and name tag look. Only fields of the <see cref="PingInstance" /> are changed, never its
///     visibility or colour: those calls are sent to the server as the player's ping preference.
/// </summary>
/// <param name="Frozen">The marker stays where contact was lost instead of following the partner.</param>
public readonly record struct PartnerSignalLook(string Rule, float MinDist, float Range, bool NameTagVisible, bool Frozen = false)
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
    public static readonly List<IPartnerSignalRule> Rules = [new LastContactRule(), new FaintSignalRule()];

    /// <summary>
    ///     The name of the object <c>PlayerModelManager.AttachPing</c> creates for a player's marker.
    /// </summary>
    public static string MarkerObjectName(string playerName) => $"signal_{playerName}";

    /// <summary>
    ///     Whether a <see cref="PingInstance" /> under a partner's body is their marker, not the ping of something they
    ///     carry (a beacon in their inventory sits under the body too, COOP_FEATURES_SPEC D2).
    /// </summary>
    public static bool IsPartnerMarker(string objectName, string playerName) => objectName == MarkerObjectName(playerName);

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

/// <summary>
///     COOP_FEATURES_SPEC D2, "the deep cuts the line": while either player is in signal interference, the faint marker
///     stays where contact was lost and flickers slowly. Up close nothing changes (the faint rule answers), and the
///     setting "Always" ignores interference.
/// </summary>
public sealed class LastContactRule : IPartnerSignalRule
{
    public const string NAME = "last contact";

    /// <summary>
    ///     One flicker every this many seconds: the marker is gone for <see cref="FLICKER_OFF" /> at the end of each.
    /// </summary>
    public const float FLICKER_PERIOD = 3f;

    public const float FLICKER_OFF = 0.5f;

    public bool TryGetLook(in PartnerSignalContext context, out PartnerSignalLook look)
    {
        look = default;
        if (!context.LinkCut || context.Mode != PartnerSignalMode.FAINT || IsUpClose(context))
        {
            return false;
        }
        bool nameTagVisible = context.Distance <= context.NameTagRange;
        float minDist = IsFlickerOff(context.SecondsSinceContact) ? PartnerSignalLook.HIDDEN : FaintSignalRule.MIN_DIST;
        look = new PartnerSignalLook(NAME, minDist, FaintSignalRule.RANGE, nameTagVisible, Frozen: true);
        return true;
    }

    /// <summary>
    ///     Where the faint rule hides the marker anyway (close, or in plain sight): the partner is right there.
    /// </summary>
    public static bool IsUpClose(in PartnerSignalContext context) =>
        context.Distance < FaintSignalRule.MIN_DIST || (context.InLineOfSight && context.Distance < FaintSignalRule.LINE_OF_SIGHT_DIST);

    public static bool IsFlickerOff(float secondsSinceContact) => secondsSinceContact % FLICKER_PERIOD >= FLICKER_PERIOD - FLICKER_OFF;

    /// <summary>
    ///     How long ago contact was lost, for the label: whole seconds under a minute, else whole minutes.
    /// </summary>
    public static (int Value, bool Minutes) Age(float secondsSinceContact)
    {
        int seconds = Math.Max(0, (int)secondsSinceContact);
        return seconds < 60 ? (seconds, false) : (seconds / 60, true);
    }
}
