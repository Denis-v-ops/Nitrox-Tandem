using System.Collections;
using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using UWE;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Only the crafting player (exclusive lock) picks the item up at the end of crafting; it drops to a transient lock once the
/// item is taken
/// </summary>
public sealed partial class GhostCrafter_OnCraftingEnd_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((GhostCrafter t) => t.OnCraftingEnd());

    public static bool Prefix(GhostCrafter __instance)
    {
        // The OnCraftingEnd patch is executed when crafting is complete and the item is about to be automatically
        // picked up by the nearest player.  We don't want all players to attempt to pick up the item.  Instead,
        // the crafting player will intiate a lock request when pushing the craft button - OnCraftingStart().

        // See GhostCrafter_OnCraftingBegin_Patch.Postfix to know why we get NitroxId on CrafterLogic
        return __instance._logic.TryGetIdOrWarn(out NitroxId id) && Resolve<SimulationOwnership>().HasExclusiveLock(id);
    }

    public static void Postfix(GhostCrafter __instance, bool __runOriginal)
    {
        if (__runOriginal && __instance._logic.TryGetNitroxId(out NitroxId id))
        {
            CoroutineHost.StartCoroutine(ReleaseLockAfterPickup(__instance._logic, id));
        }
    }

    /// <summary>
    ///     Once the item is taken (or left in the crafter, out of range), crafting no longer needs the exclusive lock. Keeping
    ///     it until then means a partner clicking the item meanwhile is refused (<see cref="GhostCrafter_OnHandClick_Patch" />,
    ///     COOP_FEATURES_SPEC B2).
    /// </summary>
    public static IEnumerator ReleaseLockAfterPickup(CrafterLogic logic, NitroxId crafterId)
    {
        while (logic && logic.pickingUp)
        {
            yield return null;
        }
        if (Resolve<SimulationOwnership>().HasExclusiveLock(crafterId))
        {
            Resolve<SimulationOwnership>().RequestSimulationLock(crafterId, SimulationLockType.TRANSIENT);
        }
    }
}
