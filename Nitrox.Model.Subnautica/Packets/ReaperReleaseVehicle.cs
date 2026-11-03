using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A Reaper Leviathan let go of the vehicle it held (MULTINAUGHTICA_SPEC M4, see <see cref="ReaperGrabVehicle" />).
///     Sent by the game whose copy let go (its timer ran out, it took electrical or poison damage); the other copies let go
///     too.
/// </summary>
[Serializable]
public sealed class ReaperReleaseVehicle : Packet
{
    public NitroxId ReaperId { get; }

    public ReaperReleaseVehicle(NitroxId reaperId)
    {
        ReaperId = reaperId;
    }
}
