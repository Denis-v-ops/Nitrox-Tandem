using System.Collections.Generic;

namespace NitroxClient.GameLogic;

/// <summary>
///     Where a ping lands (MULTINAUGHTICA_SPEC M9, after MultiNaughtica's <c>QuickPingAim</c>), as pure functions: the
///     nearest hit of the crosshair ray that isn't the player's own body, the vehicle they sit in or, at the helm, the
///     Cyclops they steer, and isn't nearer than <see cref="MIN_DISTANCE" />. A partner's body counts although it is a
///     trigger. <see cref="PingAim" /> reads the hits from the game.
/// </summary>
internal static class PingAimRules
{
    /// <summary>Nearer than this is the player's own helmet or a tool in the hand, not a target. Before M9 it was 3 m.</summary>
    public const float MIN_DISTANCE = 0.5f;

    /// <summary>A hit of the ping ray, described by what the decision needs.</summary>
    public readonly struct Hit
    {
        public Hit(float distance, bool isTrigger = false, bool isPartner = false, bool isOwnBody = false, bool isInOwnVehicle = false, bool isInCurrentSub = false)
        {
            Distance = distance;
            IsTrigger = isTrigger;
            IsPartner = isPartner;
            IsOwnBody = isOwnBody;
            IsInOwnVehicle = isInOwnVehicle;
            IsInCurrentSub = isInCurrentSub;
        }

        /// <summary>Metres from the camera.</summary>
        public float Distance { get; }

        public bool IsTrigger { get; }

        /// <summary>A partner's body (or what they hold): a trigger on the Useable layer.</summary>
        public bool IsPartner { get; }

        /// <summary>The local player's own body or tool.</summary>
        public bool IsOwnBody { get; }

        /// <summary>Part of the vehicle the local player sits in (Seamoth, Prawn suit).</summary>
        public bool IsInOwnVehicle { get; }

        /// <summary>Part of the sub the local player is in (a Cyclops, or a base).</summary>
        public bool IsInCurrentSub { get; }
    }

    /// <param name="atHelm">The player steers the sub they're in (the Cyclops helm), so the sub itself is in the way, not a target.</param>
    /// <returns>The index of the hit the ping lands on, -1 when there is none.</returns>
    public static int SelectHit(IReadOnlyList<Hit> hits, bool atHelm)
    {
        int best = -1;
        for (int i = 0; i < hits.Count; i++)
        {
            if (IsSkipped(hits[i], atHelm))
            {
                continue;
            }
            if (best < 0 || hits[i].Distance < hits[best].Distance)
            {
                best = i;
            }
        }
        return best;
    }

    public static bool IsSkipped(Hit hit, bool atHelm)
    {
        // Also skips a distance that is no number.
        if (!(hit.Distance >= MIN_DISTANCE))
        {
            return true;
        }
        // The partner's body is a trigger, and may stand in the Cyclops the player steers.
        if (hit.IsPartner)
        {
            return false;
        }
        if (hit.IsTrigger)
        {
            return true;
        }
        return hit.IsOwnBody || hit.IsInOwnVehicle || (atHelm && hit.IsInCurrentSub);
    }
}
