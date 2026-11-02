#if DEBUG
using System.Reflection;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Windowless test games (Unity batch mode, tools/dual-client.ps1 -Windowless) render no camera, so no renderer is
///     ever visible. <see cref="PlayerCinematicController.EndCinematicMode" /> leaves the cinematic's animator on
///     <see cref="AnimatorCullingMode.CullCompletely" />, which then freezes its state machine for good: the Cyclops docking
///     bay stayed in its launch state after an undock and never reached "opened", which
///     <see cref="VehicleDockingBay.OnTriggerEnter" /> requires, so a second dock never started (S6). In batch mode the
///     animator keeps animating instead, like it does in a windowed game whenever it's on screen.
/// </summary>
public sealed partial class PlayerCinematicController_EndCinematicMode_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((PlayerCinematicController t) => t.EndCinematicMode());

    public static void Postfix(PlayerCinematicController __instance)
    {
        if (Application.isBatchMode && __instance.animator)
        {
            __instance.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }
}
#endif
