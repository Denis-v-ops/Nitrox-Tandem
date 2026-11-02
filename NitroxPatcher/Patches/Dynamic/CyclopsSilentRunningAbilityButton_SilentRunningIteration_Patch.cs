using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Silent running runs in both games (the partner's game starts it from <c>CyclopsMetadata</c>), so only the game that
///     simulates the sub pays for it, as for the shield (<see cref="CyclopsShieldButton_ShieldIteration_Patch" />).
///     Otherwise both games take the power and the battery sync adds both drains up.
/// </summary>
public sealed partial class CyclopsSilentRunningAbilityButton_SilentRunningIteration_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsSilentRunningAbilityButton t) => t.SilentRunningIteration());

    public static bool Prefix(CyclopsSilentRunningAbilityButton __instance)
    {
        return __instance.subRoot.TryGetNitroxId(out NitroxId id) && Resolve<SimulationOwnership>().HasAnyLockType(id);
    }
}
