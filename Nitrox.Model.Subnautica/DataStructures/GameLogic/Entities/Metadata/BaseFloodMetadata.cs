using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;
using Nitrox.Model.DataStructures;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
///     Water level of every cell of a base (BaseFloodSim.cellWaterLevel), so that a player who joins later or reconnects
///     sees the same flooding instead of a dry base.
/// </summary>
[Serializable]
[DataContract]
public class BaseFloodMetadata : EntityMetadata
{
    /// <summary>
    ///     Size of the flood grid the levels belong to (BaseFloodSim.shape). Levels for another size are stale (the base
    ///     was rebuilt since) and must not be applied.
    /// </summary>
    [DataMember(Order = 1)]
    public NitroxInt3 GridSize { get; }

    /// <summary>
    ///     One level per grid cell, in the game's own index order (Grid3Shape.GetIndex). Empty when the base is dry.
    /// </summary>
    [DataMember(Order = 2)]
    public float[] CellWaterLevels { get; }

    public bool IsDry => CellWaterLevels == null || CellWaterLevels.Length == 0;

    [IgnoreConstructor]
    protected BaseFloodMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public BaseFloodMetadata(NitroxInt3 gridSize, float[] cellWaterLevels)
    {
        GridSize = gridSize;
        CellWaterLevels = cellWaterLevels ?? [];
    }

    public override string ToString()
    {
        return $"[{nameof(BaseFloodMetadata)} {nameof(GridSize)}: {GridSize}, {nameof(CellWaterLevels)}: {(IsDry ? "dry" : CellWaterLevels.Length.ToString())}]";
    }
}
