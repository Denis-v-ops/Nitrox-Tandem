using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Ignores colliders inside a Cyclops, so they don't play (and use up) the local player's sound trigger. Through Nitrox' parenting of the local player to its Cyclops, a collider inside a Cyclops would be taken for the local player (FIX_SPEC S6).
/// </summary>
public sealed partial class PlayerSoundTrigger_OnTriggerEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PlayerSoundTrigger t) => t.OnTriggerEnter(default));

    public static bool Prefix(Collider collider)
    {
        return !CyclopsTriggerFilter.IsInsideCyclopsButNotLocalPlayer(collider);
    }
}
