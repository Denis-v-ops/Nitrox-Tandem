using System;
using BinaryPack.Attributes;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

[Serializable]
public class SleepStatusUpdate : Packet
{
    public int PlayersInBed { get; }
    public int TotalPlayers { get; }

    /// <summary>
    ///     Names of the connected players who aren't in bed yet, so the others know who they're waiting for.
    /// </summary>
    public string[] AwakePlayerNames { get; }

    [IgnoredMember]
    public bool AllPlayersInBed => PlayersInBed >= TotalPlayers;

    public SleepStatusUpdate(int playersInBed, int totalPlayers, string[] awakePlayerNames)
    {
        PlayersInBed = playersInBed;
        TotalPlayers = totalPlayers;
        AwakePlayerNames = awakePlayerNames;
    }
}
