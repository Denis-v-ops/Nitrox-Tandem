using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A Reaper Leviathan grabbed a Seamoth or a Prawn suit (MULTINAUGHTICA_SPEC M4). Sent by the game where it happened
///     (its copy of the Reaper touched the vehicle there); the other games that see both let their copy grab the same
///     vehicle. Temporary, nothing is stored.
/// </summary>
[Serializable]
public sealed class ReaperGrabVehicle : Packet
{
    public NitroxId ReaperId { get; }
    public NitroxId VehicleId { get; }

    public ReaperGrabVehicle(NitroxId reaperId, NitroxId vehicleId)
    {
        ReaperId = reaperId;
        VehicleId = vehicleId;
    }
}
