using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     A Sea Emperor baby that this game didn't hatch (the simulating game's networked baby, spawned here under a cell root)
///     has to swim to its mother here too, or this game's Emperor never plays the babies' visit and its babies never drop
///     cure balls (FIX_SPEC S17, COOP_FEATURES_SPEC G3c). Babies still in an egg are left alone: the egg animation sends
///     them (<c>IncubatorEggAnimation.OnHatchAnimationEnd</c>); so are the temporary babies of the local hatching and the
///     game's own non-networked babies (<c>SeaEmperorBabiesSpawner</c>).
/// </summary>
/// <remarks>
///     Before, any baby with a parent was skipped, but <c>DefaultWorldEntitySpawner</c> parents every spawned world entity
///     to its cell root, so the partner's babies never went to their mother (seen windowless 2026-11-03: parent
///     <c>CellRoot(Clone)</c>, no swim target, <c>SeaEmperor.main.GetBabies()</c> empty).
/// </remarks>
public sealed partial class SeaEmperorBaby_Start_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaEmperorBaby t) => t.Start());

    public static void Postfix(SeaEmperorBaby __instance)
    {
        if (!SeaEmperor.main ||
            __instance.name.Contains(IncubatorEggAnimation_OnHatchAnimationEnd_Patch.TEMPORARY_BABY_MARKER) ||
            !__instance.TryGetNitroxId(out _) ||
            SeaEmperorBabies.IsInEgg(__instance))
        {
            return;
        }

        // Already on its way (to the mother, or leaving and dropping cure balls)
        if ((__instance.swimToTarget && __instance.swimToTarget.target) || (__instance.dropEnzymes && __instance.dropEnzymes.enabled))
        {
            return;
        }

        SeaEmperorBabies.SendCopyToMother(__instance);
    }
}
