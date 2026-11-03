using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Every grab and let-go of a propulsion cannon goes through this setter (<c>GrabObject</c>, <c>ReleaseGrabbedObject</c>,
///     a shot in <c>OnShoot</c>, the out-of-range release in <c>FixedUpdate</c>, loading from the inventory), and only in the
///     game of the player who uses the cannon. COOP_FEATURES_SPEC B6: tells the partner's game to show or hide the beam, and
///     gives up the exclusive lock on the object it let go of, keeping a normal one so this game goes on sending its position
///     while it drifts or flies. Before, only <c>ReleaseGrabbedObject</c> did that, so after a shot this game kept the
///     exclusive lock and the partner could never grab that object.
/// </summary>
public sealed partial class PropulsionCannon_grabbedObject_set_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Property((PropulsionCannon t) => t.grabbedObject).SetMethod;

    public static void Prefix(PropulsionCannon __instance, out GameObject __state)
    {
        __state = __instance._grabbedObject;
    }

    public static void Postfix(PropulsionCannon __instance, GameObject __state)
    {
        GameObject grabbed = __instance._grabbedObject;
        if (__state == grabbed)
        {
            return;
        }

        if (__state && __state.TryGetNitroxId(out NitroxId previousId) && Resolve<SimulationOwnership>().HasExclusiveLock(previousId))
        {
            Resolve<SimulationOwnership>().RequestSimulationLock(previousId, SimulationLockType.TRANSIENT);
        }

        Resolve<CannonEffects>().SendPropulsionGrabChanged(__instance, grabbed);
    }
}
