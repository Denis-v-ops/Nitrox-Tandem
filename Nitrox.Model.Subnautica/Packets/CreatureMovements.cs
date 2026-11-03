using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Networking;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     The poses of living creatures a player simulates, sent on a timer (FIX_SPEC S16b). The watching games put their
///     copies where the creature is now (<see cref="CreatureMovement.Position" /> carried forward by
///     <see cref="CreatureMovement.Velocity" /> for the time since <see cref="RealTime" />), and the server keeps the
///     latest pose, so a player arriving later spawns it where it is (S16c).
/// </summary>
[Serializable]
public sealed class CreatureMovements : Packet
{
    public List<CreatureMovement> Data { get; }

    /// <summary>
    ///     The sender's server-synced real time (<c>TimeManager.RealTimeElapsed</c>) when it took these poses.
    /// </summary>
    public double RealTime { get; }

    public CreatureMovements(List<CreatureMovement> data, double realTime)
    {
        Data = data;
        RealTime = realTime;
        // A newer pose replaces a lost one; reliable delivery would hold every other packet up behind a lost pose
        DeliveryMethod = NitroxDeliveryMethod.DeliveryMethod.UNRELIABLE_SEQUENCED;
        UdpChannel = UdpChannelId.MOVEMENTS;
    }
}

[Serializable]
public sealed class CreatureMovement
{
    public NitroxId Id { get; }
    public NitroxVector3 Position { get; }
    public NitroxQuaternion Rotation { get; }
    public NitroxVector3 Velocity { get; }
    public NitroxVector3 AngularVelocity { get; }

    public CreatureMovement(NitroxId id, NitroxVector3 position, NitroxQuaternion rotation, NitroxVector3 velocity, NitroxVector3 angularVelocity)
    {
        Id = id;
        Position = position;
        Rotation = rotation;
        Velocity = velocity;
        AngularVelocity = angularVelocity;
    }

    public override string ToString()
    {
        return $"[CreatureMovement Id:{Id} Position:{Position} Rotation:{Rotation} Velocity:{Velocity} AngularVelocity:{AngularVelocity}]";
    }
}
