using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     FIX_SPEC S16b: the partner's creatures' poses drive their copies here (<see cref="RemotelyControlled.UpdateCreatureMovement" />).
/// </summary>
internal sealed class CreatureMovementsProcessor(SimulationOwnership simulationOwnership) : IClientPacketProcessor<CreatureMovements>
{
    private readonly SimulationOwnership simulationOwnership = simulationOwnership;

    public Task Process(ClientProcessorContext context, CreatureMovements packet)
    {
        foreach (CreatureMovement movement in packet.Data)
        {
            // Not loaded here, or this game simulates it now
            if (!NitroxEntity.TryGetObjectFrom(movement.Id, out GameObject gameObject) || simulationOwnership.HasAnyLockType(movement.Id))
            {
                continue;
            }
            // The simulating game can still send some before learning that we killed it or that it turned into something
            // else with the same id (e.g. a cooked fish)
            if (!IsLivingCreature(gameObject))
            {
                continue;
            }

            RemotelyControlled.Ensure(gameObject).UpdateCreatureMovement(movement.Position.ToUnity(), movement.Rotation.ToUnity(), movement.Velocity.ToUnity(),
                                                                          movement.AngularVelocity.ToUnity(), packet.RealTime);
        }
        return Task.CompletedTask;
    }

    private static bool IsLivingCreature(GameObject gameObject)
    {
        if (!gameObject.GetComponent<SwimBehaviour>() && !gameObject.GetComponent<WalkBehaviour>())
        {
            return false;
        }

        return !gameObject.TryGetComponent(out LiveMixin liveMixin) || liveMixin.IsAlive();
    }
}
