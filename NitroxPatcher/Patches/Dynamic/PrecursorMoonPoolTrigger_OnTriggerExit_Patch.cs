using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Ignores colliders inside a Cyclops, so they don't take this moon pool's water plane away from the local player. Through Nitrox' parenting of the local player to its Cyclops, a collider inside a Cyclops would be taken for the local player (FIX_SPEC S6).
/// </summary>
public sealed partial class PrecursorMoonPoolTrigger_OnTriggerExit_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorMoonPoolTrigger t) => t.OnTriggerExit(default));

    public static bool Prefix(Collider col)
    {
        return !CyclopsTriggerFilter.IsInsideCyclopsButNotLocalPlayer(col);
    }
}
