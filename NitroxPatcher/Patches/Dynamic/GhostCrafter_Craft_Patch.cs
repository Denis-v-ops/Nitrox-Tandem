using System;
using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Crafting from base and Cyclops lockers (MULTINAUGHTICA_SPEC M8): when the inventory alone can't pay, the craft waits
///     for the server to hand over the exact locker items (<see cref="LockerCrafting.OnCraft" />) and starts when the claim
///     is granted. While the vanilla method runs without a claim the lockers don't count; the finalizer ends that.
/// </summary>
public sealed partial class GhostCrafter_Craft_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((GhostCrafter t) => t.Craft(default(TechType), default(float)));

    public static bool Prefix(GhostCrafter __instance, TechType techType, float duration)
    {
        return Resolve<LockerCrafting>().OnCraft(__instance, techType, duration);
    }

    public static Exception Finalizer(Exception __exception)
    {
        Resolve<LockerCrafting>().OnCraftFinished();
        return __exception;
    }
}
