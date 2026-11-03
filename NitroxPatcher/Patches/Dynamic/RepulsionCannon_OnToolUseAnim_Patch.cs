using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC B6: a repulsion shot (only fired with charge left, as in the game) plays the muzzle flash, bubbles
///     and sound on the partner's copy of the cannon. What the shot pushes moves in both games through
///     <see cref="RepulsionCannon_ShootObject_Patch" />.
/// </summary>
public sealed partial class RepulsionCannon_OnToolUseAnim_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((RepulsionCannon t) => t.OnToolUseAnim(default));

    public static void Prefix(RepulsionCannon __instance, out bool __state)
    {
        __state = __instance.energyMixin && __instance.energyMixin.charge > 0f;
    }

    public static void Postfix(RepulsionCannon __instance, bool __state)
    {
        if (__state)
        {
            Resolve<CannonEffects>().SendRepulsionShot(__instance);
        }
    }
}
