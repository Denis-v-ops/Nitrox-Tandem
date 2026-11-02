using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours.Gui.HUD;
using UWE;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Taking a finished item out of a fabricator by hand needs the crafter's exclusive lock (COOP_FEATURES_SPEC B2). Every
///     game shows the finished item (<c>CrafterMetadata</c>), and <see cref="CrafterLogic.TryPickup" /> creates a new item in
///     the clicking player's inventory, so two players clicking it at once would both get one. The crafting player's own
///     pickup at the end of crafting already holds that lock (<see cref="GhostCrafter_OnCraftingEnd_Patch" />).
/// </summary>
public sealed partial class GhostCrafter_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((GhostCrafter t) => t.OnHandClick(default(GUIHand)));

    public static bool Prefix(GhostCrafter __instance, GUIHand hand)
    {
        // Same conditions as the original's pickup branch; anything else (opening the crafting menu) needs no lock
        CrafterLogic logic = __instance._logic;
        if (!__instance.enabled || !logic || logic.inProgress || !__instance.HasCraftedItem() || !logic.TryGetIdOrWarn(out NitroxId crafterId))
        {
            return true;
        }

        HandInteraction<GhostCrafter> context = new(__instance, hand);
        Resolve<SimulationOwnership>().RequestSimulationLock(new LockRequest<HandInteraction<GhostCrafter>>(crafterId, SimulationLockType.EXCLUSIVE, ReceivedSimulationLockResponse, context));
        return false;
    }

    private static void ReceivedSimulationLockResponse(NitroxId crafterId, bool lockAcquired, HandInteraction<GhostCrafter> context)
    {
        GhostCrafter crafter = context.Target;
        if (!crafter)
        {
            return;
        }
        if (!lockAcquired)
        {
            crafter.gameObject.AddComponent<DenyOwnershipHand>();
            return;
        }

        // The partner may have taken the item while we waited for the lock: their CrafterMetadata came before our response
        CrafterLogic logic = crafter._logic;
        if (logic && !logic.inProgress && crafter.HasCraftedItem())
        {
            logic.TryPickup();
        }
        CoroutineHost.StartCoroutine(GhostCrafter_OnCraftingEnd_Patch.ReleaseLockAfterPickup(logic, crafterId));
    }
}
