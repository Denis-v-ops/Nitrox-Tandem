using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     FIX_SPEC S17: while a Sea Emperor baby plays a cinematic (hatching, the visit at its mother), this game's own
///     animation places it: the mother's animator moves the attach point it is parented to. A copy of a baby that another
///     game simulates would otherwise be put back every physics step to the last pose that game sent
///     (<see cref="RemotelyControlled" /> moves a kinematic body by its transform), which during a cinematic comes only every
///     2 s (a still body counts as resting). It follows that game again when the cinematic ends. The visit ends here once the
///     baby has left in that game, if this game's animation didn't end it first (<see cref="SeaEmperorBabies.EndVisitWithSimulatingGame" />).
/// </summary>
public sealed partial class SeaEmperorBabyCinematicController_SetCinematicMode_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaEmperorBabyCinematicController t) => t.SetCinematicMode(default));

    public static void Postfix(SeaEmperorBabyCinematicController __instance, bool cinematicOn)
    {
        if (__instance.baby && __instance.baby.TryGetComponent(out RemotelyControlled remotelyControlled))
        {
            remotelyControlled.enabled = !cinematicOn;
            // The visit at the mother (the only cinematic of a copy: it never plays the hatching, its temporary baby does)
            if (cinematicOn)
            {
                __instance.baby.StartCoroutine(SeaEmperorBabies.EndVisitWithSimulatingGame(__instance.baby, remotelyControlled));
            }
        }
    }
}
