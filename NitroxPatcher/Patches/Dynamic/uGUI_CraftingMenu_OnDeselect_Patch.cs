using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Crafting from base and Cyclops lockers (MULTINAUGHTICA_SPEC M8): the menu stops listening to the lockers
///     <see cref="uGUI_CraftingMenu_Open_Patch" /> subscribed it to. <c>OnDeselect</c> is where the menu drops its own
///     subscriptions; <c>Close(receiver)</c> is called by any crafter and does nothing unless the receiver is the menu's own.
/// </summary>
public sealed partial class uGUI_CraftingMenu_OnDeselect_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_CraftingMenu t) => t.OnDeselect());

    public static void Postfix()
    {
        Resolve<LockerCrafting>().OnMenuClosed();
    }
}
