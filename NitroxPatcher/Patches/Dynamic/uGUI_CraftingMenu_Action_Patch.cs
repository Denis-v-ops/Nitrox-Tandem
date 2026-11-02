using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Crafting from base and Cyclops lockers (MULTINAUGHTICA_SPEC M8): a craft that needs the lockers starts after the
///     click, once the server handed the items over, so the menu is put into its "crafting" state afterwards;
///     <see cref="LockerCrafting.OnMenuAction" /> remembers the clicked recipe for that.
/// </summary>
public sealed partial class uGUI_CraftingMenu_Action_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_CraftingMenu t) => t.Action(default));

    public static void Prefix(uGUI_CraftingMenu.Node sender)
    {
        Resolve<LockerCrafting>().OnMenuAction(sender);
    }
}
