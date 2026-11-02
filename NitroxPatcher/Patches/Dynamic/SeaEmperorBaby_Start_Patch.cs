using System.Collections;
using System.Linq;
using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// When a Sea Emperor baby is spawned on a remote client (via server broadcast),
/// it needs to swim to mother. The simulating player handles this via the egg animation
/// callback, but remote players receive the baby as a standalone entity.
///
/// This patch ensures the baby swims to mother if (<see cref="SeaEmperorBabyRules.DecideStart" />):
/// 1. The baby is not a temporary animation baby and doesn't sit under an incubator egg. The test used to be "the baby
///    has no parent", which never holds for a baby of the server: the entity spawner registers it with its cell first,
///    which parents it to the cell root before Start runs (MULTINAUGHTICA_SPEC M2).
/// 2. The baby doesn't already have a swim target (not already swimming)
/// 3. SeaEmperor.main exists (we're in the prison aquarium); when it isn't loaded yet the baby waits for it
/// </summary>
public sealed partial class SeaEmperorBaby_Start_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaEmperorBaby t) => t.Start());

    public static void Postfix(SeaEmperorBaby __instance)
    {
        SeaEmperorBabyRules.BabyOrigin origin = SeaEmperorBabyRules.Classify(__instance.name.Contains(IncubatorEggAnimation_OnHatchAnimationEnd_Patch.TEMPORARY_BABY_MARKER),
                                                                             __instance.GetComponentInParent<IncubatorEgg>());
        switch (SeaEmperorBabyRules.DecideStart(origin, SeaEmperor.main, HasSwimTarget(__instance)))
        {
            case SeaEmperorBabyRules.StartAction.SWIM_TO_MOTHER:
                SwimToMother(__instance);
                break;
            case SeaEmperorBabyRules.StartAction.WAIT_FOR_EMPEROR:
                __instance.StartCoroutine(WaitForEmperorThenSwim(__instance));
                break;
        }
    }

    private static bool HasSwimTarget(SeaEmperorBaby baby)
    {
        return baby.swimToTarget && baby.swimToTarget.target;
    }

    private static void SwimToMother(SeaEmperorBaby baby)
    {
        SeaEmperor emperor = SeaEmperor.main;
        int attachPoints = emperor.babyAttachPoints?.Length ?? 0;
        baby.SetId(SeaEmperorBabyRules.NextBabyId(emperor.GetBabies().Count(), attachPoints));
        baby.SwimToMother();
    }

    private static IEnumerator WaitForEmperorThenSwim(SeaEmperorBaby baby)
    {
        float waited = 0f;
        while (!SeaEmperor.main)
        {
            if (SeaEmperorBabyRules.EmperorWaitOver(waited))
            {
                yield break;
            }
            yield return new WaitForSeconds(1f);
            waited += 1f;
        }

        if (baby && SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.SPAWNED_BY_SERVER, true, HasSwimTarget(baby)) == SeaEmperorBabyRules.StartAction.SWIM_TO_MOTHER)
        {
            SwimToMother(baby);
        }
    }
}
