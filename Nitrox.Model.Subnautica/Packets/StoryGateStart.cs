using System;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Server → all (COOP_FEATURES_SPEC G0): the story moment may start. Each game that held it back runs it now; a game
///     that holds nothing for this kind ignores it.
/// </summary>
[Serializable]
public sealed class StoryGateStart : Packet
{
    public StoryGateKind Kind { get; }

    public StoryGateStart(StoryGateKind kind)
    {
        Kind = kind;
    }
}
