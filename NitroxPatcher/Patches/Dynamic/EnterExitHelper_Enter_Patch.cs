using System.Collections;
using System.Reflection;
using UnityEngine;
using UWE;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Makes sure a hatch entry really puts the local player in the sub (FIX_SPEC S6).
/// </summary>
/// <remarks>
/// <see cref="EnterExitHelper.Enter" /> ends in <see cref="Player.SetCurrentSub" />, which silently ignores the sub when it
/// changed less than 3 physics ticks ago (unless forced). The player then stands inside with <c>currentSub == null</c>, so the
/// game treats them as swimming and drains their oxygen until something else sets the sub.
/// </remarks>
public sealed partial class EnterExitHelper_Enter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => EnterExitHelper.Enter(default, default, default, default));

    public static void Postfix(GameObject gameObject, Player player)
    {
        if (!player || player != Player.main || !gameObject)
        {
            return;
        }
        SubRoot subRoot = global::Utils.FindAncestorWithComponent<SubRoot>(gameObject);
        if (!subRoot || player.currentSub == subRoot)
        {
            return;
        }

        if (player.cinematicModeActive)
        {
            // Hatches with an end transform call Enter while their cinematic is still playing out: heal once it's over.
            CoroutineHost.StartCoroutine(EnsureSubAfterCinematic(player, subRoot));
            return;
        }
        EnsureSub(player, subRoot);
    }

    private static IEnumerator EnsureSubAfterCinematic(Player player, SubRoot subRoot)
    {
        float giveUpTime = Time.time + 30f;
        while (player && player.cinematicModeActive && Time.time < giveUpTime)
        {
            yield return null;
        }
        if (player && subRoot && !player.cinematicModeActive && player.currentSub != subRoot && !player.GetVehicle())
        {
            EnsureSub(player, subRoot);
        }
    }

    private static void EnsureSub(Player player, SubRoot subRoot)
    {
        Log.Warn($"Entering {subRoot.name} through a hatch didn't set the local player's sub (it is {(player.currentSub ? player.currentSub.name : "none")}). Setting it.");
        player.SetCurrentSub(subRoot, true);
    }
}
