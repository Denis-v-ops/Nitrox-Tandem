using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
///     Nitrox parents the local player to the Cyclops it is in (<see cref="NitroxCyclops.OnLocalPlayerEnter" />), which
///     vanilla never does. Trigger volumes that look for a <see cref="Player" /> in the hierarchy around whatever touched them
///     (usually from its entity root, <c>UWE.Utils.GetEntityRoot</c>, which for anything inside a Cyclops is the Cyclops)
///     then find the local player when the Cyclops hull, a docked vehicle or a remote player's body touches them.
/// </summary>
public static class CyclopsTriggerFilter
{
    /// <summary>
    ///     True when the collider belongs to a Cyclops (or something inside one) and isn't the local player's own: a trigger
    ///     must not treat it as the local player (FIX_SPEC S6).
    /// </summary>
    public static bool IsInsideCyclopsButNotLocalPlayer(Collider collider)
    {
        if (!collider || !Player.main)
        {
            return false;
        }
        Transform transform = collider.transform;
        return transform.GetComponentInParent<NitroxCyclops>() && !transform.IsChildOf(Player.main.transform);
    }
}
