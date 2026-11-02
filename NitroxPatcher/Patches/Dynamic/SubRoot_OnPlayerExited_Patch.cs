using System.Diagnostics;
using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Unregisters the local player from the cyclops it leaves.
/// </summary>
public sealed partial class SubRoot_OnPlayerExited_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubRoot t) => t.OnPlayerExited(default));

    public static void Postfix(SubRoot __instance, Player player)
    {
        if (!__instance.isBase)
        {
            // FIX_SPEC S6: a player "swimming" inside a Cyclops has lost its currentSub. Log who cleared it, so a /report bundle shows the real trigger.
            float distance = player ? Vector3.Distance(player.transform.position, __instance.transform.position) : -1f;
            Log.Debug($"Local player left sub {__instance.name} (distance to its origin {distance:0.0} m, cinematic {player && player.cinematicModeActive}, vehicle {player && player.GetVehicle()}):\n{new StackTrace(1, false)}");
        }

        if (__instance.TryGetComponent(out NitroxCyclops nitroxCyclops))
        {
            nitroxCyclops.OnLocalPlayerExit();
        }
    }
}
