using System.Reflection;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Ignores colliders inside a Cyclops other than the local player's, so they don't move the local player's Cyclops room. Through Nitrox' parenting of the local player to its Cyclops, a collider inside a Cyclops would be taken for the local player (FIX_SPEC S6).
/// </summary>
public sealed partial class RoomLinks_OnTriggerEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((RoomLinks t) => t.OnTriggerEnter(default));

    public static bool Prefix(Collider col)
    {
        return !CyclopsTriggerFilter.IsInsideCyclopsButNotLocalPlayer(col);
    }
}
