using System;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Server → all (COOP_FEATURES_SPEC G0): a story moment someone asked for is waiting.
/// </summary>
[Serializable]
public sealed class StoryGateStatus : Packet
{
    public StoryGateKind Kind { get; }

    /// <summary>The players who asked for the moment and are waiting.</summary>
    public string[] ReadyNames { get; }

    /// <summary>
    ///     Who it waits for: the roster players who are offline if <see cref="Locked" />, else the ones who haven't
    ///     confirmed or aren't where the moment happens.
    /// </summary>
    public string[] MissingNames { get; }

    /// <summary>Someone on the roster is offline, so the moment can't start until they're back.</summary>
    public bool Locked { get; }

    public StoryGateStatus(StoryGateKind kind, string[] readyNames, string[] missingNames, bool locked)
    {
        Kind = kind;
        ReadyNames = readyNames;
        MissingNames = missingNames;
        Locked = locked;
    }
}
