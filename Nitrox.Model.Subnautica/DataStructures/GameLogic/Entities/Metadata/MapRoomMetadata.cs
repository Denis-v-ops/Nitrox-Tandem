using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BinaryPack.Attributes;
using Nitrox.Model.DataStructures;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
///     State of a scanner room (MapRoomFunctionality) that both players share: the resource it scans for and the ids of
///     the camera drones docked in it (in docking point order).
/// </summary>
[Serializable]
[DataContract]
public class MapRoomMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public NitroxTechType TypeToScan { get; }

    [DataMember(Order = 2)]
    public List<NitroxId> DockedCameraIds { get; } = [];

    [IgnoreConstructor]
    protected MapRoomMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public MapRoomMetadata(NitroxTechType typeToScan, List<NitroxId> dockedCameraIds)
    {
        TypeToScan = typeToScan;
        DockedCameraIds = dockedCameraIds;
    }

    public override string ToString()
    {
        return $"[{nameof(MapRoomMetadata)} TypeToScan: {TypeToScan}, DockedCameraIds: [{string.Join(", ", DockedCameraIds)}]]";
    }
}
