using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     M4 (FIX_SPEC S15a): the simulating game's Crabsquid cast its EMP blast. This game's copy casts the same vanilla blast
///     where it is, so this game's vehicles, held tool and Cyclops in range are hit. Only the blast: the creature's attack
///     state (target light, wind-up, interval) stays with the simulating game.
/// </summary>
internal sealed class EmpFiredProcessor : IClientPacketProcessor<EmpFired>
{
    public Task Process(ClientProcessorContext context, EmpFired packet)
    {
        if (!NitroxEntity.TryGetObjectFrom(packet.CreatureId, out GameObject creatureObject))
        {
            return Task.CompletedTask;
        }

        EMPAttack empAttack = creatureObject.GetComponentInChildren<EMPAttack>(true);
        LiveMixin liveMixin = creatureObject.GetComponentInParent<LiveMixin>();
        if (!empAttack || (liveMixin && !liveMixin.IsAlive()))
        {
            return Task.CompletedTask;
        }

        using (PacketSuppressor<EmpFired>.Suppress())
        {
            empAttack.CastEMPBlast();
        }
        return Task.CompletedTask;
    }
}
