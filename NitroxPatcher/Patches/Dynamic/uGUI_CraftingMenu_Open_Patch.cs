using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Crafting from base and Cyclops lockers (MULTINAUGHTICA_SPEC M8): the crafting menu refreshes which recipes it can
///     craft when the inventory changes (<c>uGUI_CraftingMenu.InventoryChanged</c>). While the menu of a station in a base
///     or Cyclops is open it also listens to that sub root's lockers (<see cref="LockerCrafting.OnMenuOpened" />), so a
///     recipe goes dark when the partner takes its ingredients out of a locker.
/// </summary>
public sealed partial class uGUI_CraftingMenu_Open_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_CraftingMenu t) => t.Open(default, default));

    public static void Postfix(uGUI_CraftingMenu __instance)
    {
        Resolve<LockerCrafting>().OnMenuOpened(__instance);
    }
}
