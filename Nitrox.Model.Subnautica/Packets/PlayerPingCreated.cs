using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A player's ping (COOP_FEATURES_SPEC F2). The server only relays it. The receiving game builds the label in its own
///     language from <see cref="Kind" /> and <see cref="TargetTechType" />.
/// </summary>
[Serializable]
public class PlayerPingCreated(SessionId sessionId, PingKind kind, NitroxTechType targetTechType, NitroxVector3 position, NitroxId pingId)
    : Packet
{
    public SessionId SessionId { get; } = sessionId;
    public PingKind Kind { get; } = kind;
    /// <summary>
    ///     What was pinged: the creature for <see cref="PingKind.Danger" />, the item for <see cref="PingKind.Look" /> (its
    ///     name and icon) or <c>Player</c> for a partner (each game names the player nearest the ping other than the
    ///     sender), <see cref="NitroxTechType.None" /> for <see cref="PingKind.ComeHere" />.
    /// </summary>
    public NitroxTechType TargetTechType { get; } = targetTechType;
    public NitroxVector3 Position { get; } = position;
    public NitroxId PingId { get; } = pingId;
}
