using System.Reflection;
using NitroxClient.GameLogic.Bases;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     A camera drone taken out of its docking point: the scanner room's docked drones change for everyone. The drone
///     itself goes through the normal pickup sync. Undocking to pilot the drone is not broadcast (piloting isn't synced).
/// </summary>
public sealed partial class MapRoomCamera_OnPickedUp_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCamera t) => t.OnPickedUp(default));

    public static void Prefix(MapRoomCamera __instance, out MapRoomCameraDocking __state)
    {
        __state = __instance.dockingPoint;
    }

    public static void Postfix(MapRoomCameraDocking __state)
    {
        if (__state)
        {
            MapRoomCameras.BroadcastDockedCameras(__state);
        }
    }
}
