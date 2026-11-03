using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC B6: a shot of a grabbed object plays the muzzle flash and sound on the partner's copy of the cannon.
///     (Without a grabbed object, <c>OnShoot</c> tries a grab, which <see cref="PropulsionCannon_grabbedObject_set_Patch" />
///     reports.)
/// </summary>
public sealed partial class PropulsionCannon_OnShoot_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PropulsionCannon t) => t.OnShoot());

    public static void Prefix(PropulsionCannon __instance, out bool __state)
    {
        __state = __instance.grabbedObject;
    }

    public static void Postfix(PropulsionCannon __instance, bool __state)
    {
        if (__state && !__instance.grabbedObject)
        {
            Resolve<CannonEffects>().SendPropulsionShot(__instance);
        }
    }
}
