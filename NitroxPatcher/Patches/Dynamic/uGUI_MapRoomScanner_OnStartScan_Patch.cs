using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Shares the resource a scanner room scans for (#2410). Hooked on the UI, not on
///     <see cref="MapRoomFunctionality.StartScanning" />, which <see cref="MapRoomFunctionality" />.Start also calls.
/// </summary>
public sealed partial class uGUI_MapRoomScanner_OnStartScan_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_MapRoomScanner t) => t.OnStartScan(default));

    public static void Postfix(uGUI_MapRoomScanner __instance)
    {
        BroadcastScanTarget(__instance);
    }

    public static void BroadcastScanTarget(uGUI_MapRoomScanner scannerUI)
    {
        MapRoomFunctionality mapRoom = scannerUI.mapRoom;
        if (mapRoom && mapRoom.TryGetIdOrWarn(out NitroxId mapRoomId))
        {
            Resolve<Entities>().EntityMetadataChanged(mapRoom, mapRoomId);
        }
    }
}
