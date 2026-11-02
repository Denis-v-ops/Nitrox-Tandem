using System.Reflection;
using NitroxClient.GameLogic.Bases;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     The game spawns a new camera drone in every docking point unless the scanner room was proto-deserialized, which
///     Nitrox never does, so drones multiplied with each load and had no ids (#2576). Nitrox spawns the docked drones
///     itself, see <see cref="MapRoomCameras" />.
/// </summary>
public sealed partial class MapRoomCameraDocking_Start_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCameraDocking t) => t.Start());

    public static void Prefix(MapRoomCameraDocking __instance)
    {
        __instance.deserialized = true;
    }
}
