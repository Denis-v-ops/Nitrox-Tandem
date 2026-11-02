using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Crafting from base and Cyclops lockers (MULTINAUGHTICA_SPEC M8): the counts in a recipe's tooltip.
///     <c>TooltipFactory.WriteIngredients</c> colours each ingredient by <c>Inventory.GetPickupCount</c> (one call, game
///     83031), replaced like in <see cref="CrafterLogic_IsCraftRecipeFulfilled_Patch" />. The Habitat Builder's tooltips use
///     it too, but no crafting menu is open then, so they keep the inventory count.
/// </summary>
public sealed partial class TooltipFactory_WriteIngredients_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => TooltipFactory.WriteIngredients(default, default));

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => CrafterLogic_IsCraftRecipeFulfilled_Patch.ReplaceGetPickupCount(instructions);
}
