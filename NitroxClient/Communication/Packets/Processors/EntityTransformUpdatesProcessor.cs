using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using static Nitrox.Model.Subnautica.Packets.EntityTransformUpdates;

namespace NitroxClient.Communication.Packets.Processors;

/// <remarks>Living creatures move with <see cref="CreatureMovements" /> instead (FIX_SPEC S16b).</remarks>
internal sealed class EntityTransformUpdatesProcessor(SimulationOwnership simulationOwnership) : IClientPacketProcessor<EntityTransformUpdates>
{
    private readonly SimulationOwnership simulationOwnership = simulationOwnership;

    public Task Process(ClientProcessorContext context, EntityTransformUpdates packet)
    {
        foreach (EntityTransformUpdate update in packet.Updates)
        {
            // We will cancel any position update attempt at one of our locked entities
            if (!NitroxEntity.TryGetObjectFrom(update.Id, out GameObject gameObject) ||
                simulationOwnership.HasAnyLockType(update.Id))
            {
                continue;
            }

            RemotelyControlled.Ensure(gameObject).UpdateOrientation(update.Position.ToUnity(), update.Rotation.ToUnity());
        }
        return Task.CompletedTask;
    }
}
