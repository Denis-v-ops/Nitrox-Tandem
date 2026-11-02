using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     COOP_FEATURES_SPEC D4: a creature simulated by another player bit a vehicle whose lock we hold (usually the one we
///     pilot). Only this game may change the vehicle's health, so the damage is applied here, with this game's reductions,
///     and LiveMixin_TakeDamage_Patch broadcasts the new health as for any other damage.
/// </summary>
internal sealed class CreatureAttackedVehicleProcessor(SimulationOwnership simulationOwnership) : IClientPacketProcessor<CreatureAttackedVehicle>
{
    private readonly SimulationOwnership simulationOwnership = simulationOwnership;

    public Task Process(ClientProcessorContext context, CreatureAttackedVehicle packet)
    {
        // The lock moved on while the bite was on the way: the new holder's game didn't see the bite
        if (!simulationOwnership.HasAnyLockType(packet.VehicleId) || !NitroxEntity.TryGetComponentFrom(packet.VehicleId, out LiveMixin vehicleLiveMixin) ||
            !vehicleLiveMixin.IsAlive())
        {
            return Task.CompletedTask;
        }

        GameObject creature = null;
        if (NitroxEntity.TryGetObjectFrom(packet.CreatureId, out GameObject creatureObject))
        {
            // We killed it while its bite was on the way
            if (creatureObject.TryGetComponent(out LiveMixin creatureLiveMixin) && !creatureLiveMixin.IsAlive())
            {
                return Task.CompletedTask;
            }
            creature = creatureObject;
        }

        vehicleLiveMixin.TakeDamage(packet.Damage, default, (DamageType)packet.DamageType, creature);
        return Task.CompletedTask;
    }
}
