using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Bases;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Only one game creates the salt and water of a filtration machine (#2495, FIX_SPEC S7): the one holding the base's
/// simulation lock (<see cref="BaseModuleSimulation"/>). Its new item is announced once, as an item of the machine's
/// container (see <see cref="Items.MachineProducingCount"/>); the other games get it from the server.
/// </summary>
/// <remarks>
/// <c>FiltrationMachine.Spawn</c> (game 83031) checks for room in this game's copy of the container, instantiates the
/// prefab, calls <c>Pickupable.Pickup(false)</c> and <c>container.UnsafeAdd</c>. <c>UpdateFiltering</c>, the
/// <c>DelayedStart</c> and the <c>filterwater</c>/<c>filtersalt</c> console commands all go through it. The timers keep
/// running in every game (they also draw the base's power, which isn't synced), only the item creation is skipped.
/// </remarks>
public sealed partial class FiltrationMachine_Spawn_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((FiltrationMachine t) => t.Spawn(default));

    public static bool Prefix(FiltrationMachine __instance, ref bool __result, out bool __state)
    {
        __state = Resolve<BaseModuleSimulation>().IsLocalProducer(__instance);
        if (!__state)
        {
            __result = false;
            return false;
        }

        Resolve<Items>().MachineProducingCount++;
        return true;
    }

    public static void Finalizer(bool __state)
    {
        if (__state)
        {
            Resolve<Items>().MachineProducingCount--;
        }
    }
}
