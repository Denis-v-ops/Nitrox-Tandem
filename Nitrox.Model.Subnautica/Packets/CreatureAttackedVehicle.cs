using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A creature bit a vehicle (Seamoth, Prawn suit, Cyclops) in the game that simulates the creature, but another
///     player's game holds the vehicle's lock, and only that game applies damage to it (COOP_FEATURES_SPEC D4, the
///     vehicle side of FIX_SPEC S14). The server relays it only from the creature's simulating player, and only to the
///     vehicle's lock holder, whose game applies the damage and broadcasts the vehicle's new health.
/// </summary>
[Serializable]
public sealed class CreatureAttackedVehicle : Packet
{
    public NitroxId CreatureId { get; }
    public NitroxId VehicleId { get; }

    /// <summary>
    ///     Damage before the vehicle's own reductions, which the receiving game applies.
    /// </summary>
    public float Damage { get; }

    /// <summary>
    ///     The game's <c>DamageType</c>.
    /// </summary>
    public ushort DamageType { get; }

    public CreatureAttackedVehicle(NitroxId creatureId, NitroxId vehicleId, float damage, ushort damageType)
    {
        CreatureId = creatureId;
        VehicleId = vehicleId;
        Damage = damage;
        DamageType = damageType;
    }
}
