using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

[Serializable, DataContract]
public class RadiationMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public float Health { get; set; }

    [DataMember(Order = 2)]
    public float FixRealTime { get; set; }

    [IgnoreConstructor]
    protected RadiationMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public RadiationMetadata(float health, float fixRealTime = -1)
    {
        Health = health;
        FixRealTime = fixRealTime;
    }

    /// <summary>
    ///     A leak only ever gains health (it's created at 0 and welded): an update that doesn't raise it is older than what is
    ///     known (two players welding the same leak, an update still on its way) and would make a repaired leak leak again,
    ///     and set its fix time back to "leaking" on the server (MULTINAUGHTICA_SPEC M2).
    /// </summary>
    public static bool IsProgress(float currentHealth, float newHealth) => newHealth > currentHealth;

    public override string ToString()
    {
        return $"[{nameof(RadiationMetadata)} Health: {Health}, FixRealTime: {FixRealTime}]";
    }
}
