using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5). A player's game sends it to grab on to the Seamoth a partner pilots
///     (<see cref="PlayerId" /> is ignored then: the server takes the sender). The server checks it and sends it to
///     everyone, the hitcher's game included, which only grabs on when it arrives. A player who joins later gets one for
///     each player who holds on to a Seamoth.
/// </summary>
[Serializable]
public sealed class PlayerHitched : Packet
{
    public SessionId PlayerId { get; }

    public NitroxId VehicleId { get; }

    public PlayerHitched(SessionId playerId, NitroxId vehicleId)
    {
        PlayerId = playerId;
        VehicleId = vehicleId;
    }
}
