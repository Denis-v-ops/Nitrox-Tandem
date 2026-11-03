using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

[Serializable]
[DataContract]
public class EscapePodMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public bool PodRepaired { get; }

    [DataMember(Order = 2)]
    public bool RadioRepaired { get; }

    /// <summary>
    ///     Names of the players who have opened the bottom hatch once (its first-use cinematic plays once per player). The
    ///     name is how the server keys saved players; a session id is only a connection index and is reused.
    /// </summary>
    [DataMember(Order = 3)]
    public List<string> PlayersWithBottomHatchUsed { get; } = [];

    /// <inheritdoc cref="PlayersWithBottomHatchUsed" />
    [DataMember(Order = 4)]
    public List<string> PlayersWithTopHatchUsed { get; } = [];

    [IgnoreConstructor]
    protected EscapePodMetadata()
    {
        //Constructor for serialization. Has to be "protected" for json serialization.
    }

    public EscapePodMetadata(bool podRepaired, bool radioRepaired, List<string> playersWithBottomHatchUsed, List<string> playersWithTopHatchUsed)
    {
        PodRepaired = podRepaired;
        RadioRepaired = radioRepaired;
        // Null when a save from before R1b is loaded (the JSON serializer passes missing members as null)
        PlayersWithBottomHatchUsed = playersWithBottomHatchUsed ?? [];
        PlayersWithTopHatchUsed = playersWithTopHatchUsed ?? [];
    }

    public override string ToString()
    {
        return $"[{nameof(EscapePodMetadata)} - PodRepaired: {PodRepaired}, RadioRepaired: {RadioRepaired}, PlayersWithBottomHatchUsed: {PlayersWithBottomHatchUsed.Count}, PlayersWithTopHatchUsed: {PlayersWithTopHatchUsed.Count}]";
    }
}
