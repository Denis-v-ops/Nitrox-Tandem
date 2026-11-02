using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Crafting from base and Cyclops lockers (MULTINAUGHTICA_SPEC M8): the craft a granted claim starts spends only the
///     inventory's share of the ingredients; the server removed the locker items before the answer
///     (<see cref="LockerCrafting.OnConsumeResources" />). Every other craft is vanilla.
/// </summary>
public sealed partial class CrafterLogic_ConsumeResources_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => CrafterLogic.ConsumeResources(default));

    public static bool Prefix(TechType techType, ref bool __result)
    {
        return Resolve<LockerCrafting>().OnConsumeResources(techType, ref __result);
    }
}
