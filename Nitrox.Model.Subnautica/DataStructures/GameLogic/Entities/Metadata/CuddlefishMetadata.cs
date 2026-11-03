using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

/// <summary>
///     A cuddlefish (CuteFish): whether it follows or stays, and whether the farewell was played (it then stays for good).
///     The cuddlefish also has a WaterParkCreature (age, breeding) but an entity holds one metadata, so its water park
///     state travels here too (null when it has the default values).
/// </summary>
[Serializable]
[DataContract]
public class CuddlefishMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public bool FollowingPlayer { get; }

    [DataMember(Order = 2)]
    public bool GoodbyePlayed { get; }

    [DataMember(Order = 3)]
    public WaterParkCreatureMetadata WaterParkCreature { get; }

    [IgnoreConstructor]
    protected CuddlefishMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public CuddlefishMetadata(bool followingPlayer, bool goodbyePlayed, WaterParkCreatureMetadata waterParkCreature)
    {
        FollowingPlayer = followingPlayer;
        GoodbyePlayed = goodbyePlayed;
        WaterParkCreature = waterParkCreature;
    }

    public override string ToString()
    {
        return $"[{nameof(CuddlefishMetadata)} FollowingPlayer: {FollowingPlayer}, GoodbyePlayed: {GoodbyePlayed}, WaterParkCreature: {WaterParkCreature}]";
    }
}
