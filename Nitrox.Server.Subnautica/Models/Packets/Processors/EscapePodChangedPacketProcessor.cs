using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class EscapePodChangedPacketProcessor(EntityRegistry entityRegistry) : IAuthPacketProcessor<EscapePodChanged>
{
    public async Task Process(AuthProcessorContext context, EscapePodChanged packet)
    {
        // "No escape pod" (the game drops the pod by distance) must not take the player out of a sub root (PlayerParentRules)
        if (!PlayerParentRules.KeepsSubRootParent(packet.EscapePodId, PlayerParentRules.GetParent(entityRegistry, context.Sender.GameObjectId)))
        {
            entityRegistry.ReparentEntity(context.Sender.GameObjectId, packet.EscapePodId.OrNull());
            context.Sender.SubRootId = packet.EscapePodId;
        }
        await context.SendToOthersAsync(packet);
    }
}
