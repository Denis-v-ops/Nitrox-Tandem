using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.GameLogic.Bases;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class MapRoomMetadataProcessor : EntityMetadataProcessor<MapRoomMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, MapRoomMetadata metadata)
    {
        if (!gameObject.TryGetComponent(out MapRoomFunctionality mapRoom))
        {
            Log.Error($"[{nameof(MapRoomMetadataProcessor)}] Could not find {nameof(MapRoomFunctionality)} on {gameObject.name}");
            return;
        }

        TechType typeToScan = metadata.TypeToScan.ToUnity();
        if (mapRoom.typeToScan != typeToScan)
        {
            using (PacketSuppressor<EntityMetadataUpdate>.Suppress())
            {
                // What uGUI_MapRoomScanner.OnStartScan/OnCancelScan do (their patches broadcast, hence the suppressor)
                mapRoom.StartScanning(typeToScan);
                foreach (uGUI_MapRoomScanner scannerUI in mapRoom.GetComponentsInChildren<uGUI_MapRoomScanner>(true))
                {
                    scannerUI.UpdateGUIState();
                }
            }
        }

        MapRoomCameras.Apply(mapRoom, metadata.DockedCameraIds);
    }
}
