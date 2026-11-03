using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

[Serializable]
[DataContract]
public class ThermalPlantMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public float Temperature { get; }

    /// <summary>
    ///     Charge of the plant's power source (COOP_FEATURES_SPEC B3, #1811), sent by the game holding the plant's lock.
    ///     Null in saves from before B3 and in temperature-only updates: the power is then left as it is (filled to the
    ///     maximum when the plant spawns).
    /// </summary>
    [DataMember(Order = 2)]
    public float? Power { get; }

    [IgnoreConstructor]
    protected ThermalPlantMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public ThermalPlantMetadata(float temperature, float? power)
    {
        Temperature = temperature;
        Power = power;
    }

    public override string ToString()
    {
        return $"[{nameof(ThermalPlantMetadata)} {nameof(Temperature)}: {Temperature} {nameof(Power)}: {Power}]";
    }
}
