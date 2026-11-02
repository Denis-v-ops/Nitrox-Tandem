using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     <see cref="uGUI_MapRoomScanner.OnDisable" /> only cancels the scan, when the screen turns off because the room has
///     no power. Power isn't shared and a room is unpowered for a moment after every load, so this cancelled the shared
///     scan on every join (seen in game). In multiplayer, the scan only stops through the cancel button.
/// </summary>
public sealed partial class uGUI_MapRoomScanner_OnDisable_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_MapRoomScanner t) => t.OnDisable());

    public static bool Prefix()
    {
        return false;
    }
}
