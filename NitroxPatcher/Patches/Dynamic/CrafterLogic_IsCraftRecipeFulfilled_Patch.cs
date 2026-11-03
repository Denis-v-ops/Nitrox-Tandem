using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NitroxClient.GameLogic;
using NitroxPatcher.PatternMatching;
using static System.Reflection.Emit.OpCodes;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Crafting from base and Cyclops lockers (MULTINAUGHTICA_SPEC M8): which recipes light up in the crafting menu.
///     <c>CrafterLogic.IsCraftRecipeFulfilled</c> counts each ingredient with <c>Inventory.GetPickupCount</c> (one call, game
///     83031); the call is replaced by <see cref="GetCount" />, which adds the locker items while the menu of a station in a
///     base or Cyclops is open and is the vanilla count otherwise.
/// </summary>
public sealed partial class CrafterLogic_IsCraftRecipeFulfilled_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => CrafterLogic.IsCraftRecipeFulfilled(default));

    /// <summary>The one <c>Inventory.GetPickupCount</c> call (callvirt); shared with <see cref="TooltipFactory_WriteIngredients_Patch" />.</summary>
    internal static readonly InstructionsPattern GetPickupCountPattern = new()
    {
        { Reflect.Method((Inventory t) => t.GetPickupCount(default)), "GetPickupCount" }
    };

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceGetPickupCount(instructions);

    /// <summary>
    ///     The receiver and the tech type are already on the stack for the instance call, so a static call with the same two
    ///     parameters takes its place.
    /// </summary>
    internal static IEnumerable<CodeInstruction> ReplaceGetPickupCount(IEnumerable<CodeInstruction> instructions) => instructions
        .ChangeAtMarker(GetPickupCountPattern, "GetPickupCount", instruction =>
        {
            instruction.opcode = Call;
            instruction.operand = Reflect.Method(() => GetCount(default, default));
        });

    public static int GetCount(Inventory inventory, TechType techType) => Resolve<LockerCrafting>().GetCount(inventory, techType);
}
