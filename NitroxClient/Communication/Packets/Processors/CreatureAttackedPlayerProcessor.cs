using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     FIX_SPEC S14: a creature simulated by another player bit (or blew up next to) our body in their game. The damage is
///     applied here, to the real player, so this game's reductions (dive suit, difficulty, cheats) and hurt effects apply.
/// </summary>
internal sealed class CreatureAttackedPlayerProcessor : IClientPacketProcessor<CreatureAttackedPlayer>
{
    public Task Process(ClientProcessorContext context, CreatureAttackedPlayer packet)
    {
        Player player = Player.main;
        // The same check the creature's game made with what it knows of us (RemotePlayer.CanBeAttacked), now with our own state
        if (!player || !player.liveMixin || !player.CanBeAttacked())
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

        player.liveMixin.TakeDamage(packet.Damage, default, (DamageType)packet.DamageType, creature);
        return Task.CompletedTask;
    }
}
