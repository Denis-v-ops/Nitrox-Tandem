using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     COOP_FEATURES_SPEC B6: draws the propulsion cannon's beam on the partner's copy of the held cannon, the way
///     <c>PropulsionCannon.Update</c> draws it in the grabbing player's game (electric lines from the muzzle to the object's
///     bounds centre, the glow on the object, the grab loop sound). It doesn't set the copy's <c>grabbedObject</c>: that
///     would run the cannon's attraction physics (<c>FixedUpdate</c>) towards a point in front of this game's camera.
/// </summary>
public class RemotePropulsionBeam : MonoBehaviour
{
    public PropulsionCannon Cannon;

    private NitroxId targetId;
    private GameObject target;
    private VFXElectricLine[] lines;
    private bool shown;

    public void Show(NitroxId grabbedId)
    {
        targetId = grabbedId;
        target = null;
        // The object may not be loaded yet; Update shows the beam once it is
        SetVisible(false);
    }

    public void Hide()
    {
        targetId = null;
        target = null;
        SetVisible(false);
    }

    private void Update()
    {
        if (targetId == null || !Cannon)
        {
            return;
        }
        if (!target && !NitroxEntity.TryGetObjectFrom(targetId, out target))
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        Vector3 center = GetBoundsCenter(target);
        foreach (VFXElectricLine line in lines)
        {
            line.origin = Cannon.muzzle.position;
            line.target = center;
            line.originVector = Cannon.muzzle.forward;
        }
        if (Cannon.grabbedEffect)
        {
            Cannon.grabbedEffect.transform.position = center;
        }
    }

    private void OnDisable()
    {
        // Put away with the beam on (the holster's release also reaches this game as its own packet). No SetActive
        // here: the cannon is being deactivated; OnEnable turns the beam off.
        targetId = null;
        target = null;
    }

    private void OnEnable()
    {
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (shown == visible || !Cannon)
        {
            return;
        }
        shown = visible;
        lines ??= Cannon.fxBeam.GetComponentsInChildren<VFXElectricLine>(true);
        Cannon.fxBeam.SetActive(visible);
        if (Cannon.grabbedEffect)
        {
            Cannon.grabbedEffect.SetActive(visible);
        }
        if (Cannon.grabbingSound)
        {
            if (visible)
            {
                Cannon.grabbingSound.Play();
            }
            else
            {
                Cannon.grabbingSound.Stop();
            }
        }
    }

    /// <summary>As <c>PropulsionCannon.GetAABB</c>.</summary>
    private static Vector3 GetBoundsCenter(GameObject gameObject)
    {
        if (gameObject.TryGetComponent(out FixedBounds fixedBounds))
        {
            return fixedBounds.bounds.center;
        }
        return UWE.Utils.GetEncapsulatedAABB(gameObject, 20).center;
    }
}
