using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
///     Charge of a base power source (solar panel, bioreactor, nuclear reactor; thermal plants keep theirs in
///     <see cref="ThermalPlantMetadata.Power" />). Sent by the game holding the base's simulation lock (COOP_FEATURES_SPEC
///     B3, #1811). A module saved without it is filled to its maximum when spawned, as before.
/// </summary>
[Serializable]
[DataContract]
public class PowerSourceMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public float Power { get; }

    [IgnoreConstructor]
    protected PowerSourceMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public PowerSourceMetadata(float power)
    {
        Power = power;
    }

    public override string ToString()
    {
        return $"[{nameof(PowerSourceMetadata)} {nameof(Power)}: {Power}]";
    }
}
