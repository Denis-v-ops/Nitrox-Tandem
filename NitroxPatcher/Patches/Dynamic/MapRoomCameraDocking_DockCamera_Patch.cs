using System.Reflection;
using NitroxClient.GameLogic.Bases;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     A camera drone docked in a scanner room (a dropped drone floating into its docking point): the room's docked drones
///     change for everyone. <see cref="MapRoomCameras.Apply" /> docks drones with this broadcast suppressed.
/// </summary>
public sealed partial class MapRoomCameraDocking_DockCamera_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCameraDocking t) => t.DockCamera(default));

    public static void Postfix(MapRoomCameraDocking __instance)
    {
        MapRoomCameras.BroadcastDockedCameras(__instance);
    }
}
