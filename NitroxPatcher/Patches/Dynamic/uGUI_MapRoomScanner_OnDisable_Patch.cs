using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     <see cref="uGUI_MapRoomScanner.OnDisable" /> cancels the scan when the screen turns off (local power loss, leaving the
///     game). That's local, so it must not cancel the shared scan for everyone (see
///     <see cref="uGUI_MapRoomScanner_OnCancelScan_Patch" />).
/// </summary>
public sealed partial class uGUI_MapRoomScanner_OnDisable_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_MapRoomScanner t) => t.OnDisable());

    public static bool Running;

    public static void Prefix()
    {
        Running = true;
    }

    public static void Finalizer()
    {
        Running = false;
    }
}
