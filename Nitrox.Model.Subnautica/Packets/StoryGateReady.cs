using System;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Client → server (COOP_FEATURES_SPEC G0): this player wants the story moment <see cref="Kind" /> to start (or no
///     longer does). The game held the moment back and runs it when <see cref="StoryGateStart" /> comes. While a held
///     attempt goes on (e.g. welding), the game sends it again every few seconds, which keeps the ready alive.
/// </summary>
[Serializable]
public sealed class StoryGateReady : Packet
{
    public StoryGateKind Kind { get; }
    public bool Ready { get; }

    public StoryGateReady(StoryGateKind kind, bool ready)
    {
        Kind = kind;
        Ready = ready;
    }
}
