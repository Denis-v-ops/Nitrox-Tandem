using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Bases;

/// <summary>
///     Camera drones docked in a scanner room. The game spawns a fresh drone in every docking point on
///     <see cref="MapRoomCameraDocking.Start" /> unless the room was proto-deserialized, which never happens in Nitrox, so
///     every load added untracked drones (#2576). In multiplayer that spawn is disabled
///     (<c>MapRoomCameraDocking_Start_Patch</c>) and the docked drones are spawned from the room's metadata instead, with
///     ids derived from the room id for a room that has no metadata yet (freshly built, or saved before this existed).
/// </summary>
public static class MapRoomCameras
{
    /// <summary>
    ///     The docking points of a scanner room, in a stable order.
    /// </summary>
    public static List<MapRoomCameraDocking> GetDockings(MapRoomFunctionality mapRoom)
    {
        return mapRoom.GetComponentsInChildren<MapRoomCameraDocking>(true).ToList();
    }

    public static bool TryGetMapRoom(MapRoomCameraDocking docking, out MapRoomFunctionality mapRoom)
    {
        mapRoom = docking ? docking.GetComponentInParent<MapRoomFunctionality>(true) : null;
        return mapRoom;
    }

    /// <summary>
    ///     Sends the drones docked in the scanner room of <paramref name="docking" /> to the other players and the server.
    /// </summary>
    public static void BroadcastDockedCameras(MapRoomCameraDocking docking)
    {
        if (TryGetMapRoom(docking, out MapRoomFunctionality mapRoom) && mapRoom.TryGetNitroxId(out NitroxId mapRoomId))
        {
            NitroxServiceLocator.Cache<Entities>.Value.EntityMetadataChanged(mapRoom, mapRoomId);
        }
    }

    /// <summary>
    ///     Ids of the drones currently docked in <paramref name="mapRoom" />, in docking point order.
    /// </summary>
    public static List<NitroxId> GetDockedCameraIds(MapRoomFunctionality mapRoom)
    {
        List<NitroxId> ids = [];
        foreach (MapRoomCameraDocking docking in GetDockings(mapRoom))
        {
            MapRoomCamera camera = docking.camera.AliveOrNull();
            if (docking.cameraDocked && camera && camera.TryGetNitroxId(out NitroxId cameraId))
            {
                ids.Add(cameraId);
            }
        }
        return ids;
    }

    /// <summary>
    ///     Drones a scanner room starts with: one per docking point, with ids derived from the room id so that every player
    ///     (and every load) gives them the same ids.
    /// </summary>
    public static List<NitroxId> GetDefaultCameraIds(NitroxId mapRoomId, int dockingCount)
    {
        List<NitroxId> ids = [];
        NitroxId id = mapRoomId;
        for (int i = 0; i < dockingCount; i++)
        {
            id = id.Increment();
            ids.Add(id);
        }
        return ids;
    }

    public static void ApplyDefault(MapRoomFunctionality mapRoom)
    {
        if (!mapRoom.TryGetNitroxId(out NitroxId mapRoomId))
        {
            Log.Error($"[{nameof(MapRoomCameras)}] Scanner room {mapRoom.name} has no id, can't spawn its camera drones");
            return;
        }
        Apply(mapRoom, GetDefaultCameraIds(mapRoomId, GetDockings(mapRoom).Count));
    }

    /// <summary>
    ///     Makes the docked drones of <paramref name="mapRoom" /> exactly <paramref name="dockedCameraIds" />: drones that
    ///     shouldn't be docked are removed (they exist elsewhere as normal entities, e.g. in a player's inventory), missing ones
    ///     are docked if they already exist in this game, else spawned.
    /// </summary>
    public static void Apply(MapRoomFunctionality mapRoom, IList<NitroxId> dockedCameraIds)
    {
        List<MapRoomCameraDocking> dockings = GetDockings(mapRoom);
        HashSet<NitroxId> missing = [.. dockedCameraIds];

        using (PacketSuppressor<EntityMetadataUpdate>.Suppress())
        using (PacketSuppressor<EntityDestroyed>.Suppress())
        {
            foreach (MapRoomCameraDocking docking in dockings)
            {
                MapRoomCamera camera = docking.camera.AliveOrNull();
                if (!camera)
                {
                    docking.camera = null;
                    docking.cameraDocked = false;
                    continue;
                }
                if (camera.TryGetNitroxId(out NitroxId cameraId) && missing.Remove(cameraId))
                {
                    continue;
                }
                docking.UndockCamera();
                Object.Destroy(camera.gameObject);
            }

            foreach (NitroxId cameraId in dockedCameraIds)
            {
                if (!missing.Contains(cameraId))
                {
                    continue;
                }
                MapRoomCameraDocking freeDocking = dockings.FirstOrDefault(docking => !docking.camera.AliveOrNull());
                if (!freeDocking)
                {
                    Log.Warn($"[{nameof(MapRoomCameras)}] No free docking point in scanner room {mapRoom.name} for camera drone {cameraId}");
                    break;
                }

                if (NitroxEntity.TryGetComponentFrom(cameraId, out MapRoomCamera existingCamera))
                {
                    if (existingCamera.pickupAble.attached)
                    {
                        // In someone's inventory, so not actually docked
                        continue;
                    }
                    freeDocking.DockCamera(existingCamera);
                    continue;
                }

                GameObject cameraObject = Object.Instantiate(freeDocking.cameraPrefab);
                NitroxEntity.SetNewId(cameraObject, cameraId);
                CrafterLogic.NotifyCraftEnd(cameraObject, TechType.MapRoomCamera);
                freeDocking.DockCamera(cameraObject.GetComponent<MapRoomCamera>());
            }
        }
    }
}
