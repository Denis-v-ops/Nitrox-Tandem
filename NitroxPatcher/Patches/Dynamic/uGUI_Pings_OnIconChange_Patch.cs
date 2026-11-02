using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Keeps a player ping's own icon when its ping type changes (COOP_FEATURES_SPEC F2): <see cref="uGUI_Pings.OnIconChange" />
///     sets the icon from the ping type. See <see cref="uGUI_Pings_OnAdd_Patch" />.
/// </summary>
public sealed partial class uGUI_Pings_OnIconChange_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_Pings t) => t.OnIconChange(default));

    public static void Postfix(uGUI_Pings __instance, PingInstance instance)
    {
        uGUI_Pings_OnAdd_Patch.ApplyPlayerPing(__instance, instance);
    }
}
