using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <inheritdoc cref="uGUI_MapRoomScanner_OnStartScan_Patch" />
public sealed partial class uGUI_MapRoomScanner_OnCancelScan_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_MapRoomScanner t) => t.OnCancelScan());

    public static void Postfix(uGUI_MapRoomScanner __instance)
    {
        // Screen turning off (local), not the cancel button
        if (uGUI_MapRoomScanner_OnDisable_Patch.Running)
        {
            return;
        }
        uGUI_MapRoomScanner_OnStartScan_Patch.BroadcastScanTarget(__instance);
    }
}
