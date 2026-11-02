using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Ignores colliders inside a Cyclops, so they don't give the local player this moon pool's water plane. Through Nitrox' parenting of the local player to its Cyclops, a collider inside a Cyclops would be taken for the local player (FIX_SPEC S6).
/// </summary>
public sealed partial class PrecursorMoonPoolTrigger_OnTriggerEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorMoonPoolTrigger t) => t.OnTriggerEnter(default));

    public static bool Prefix(Collider col)
    {
        return !CyclopsTriggerFilter.IsInsideCyclopsButNotLocalPlayer(col);
    }
}
