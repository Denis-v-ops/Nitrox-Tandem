using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class SubRootChangedPacketProcessor(EntityRegistry entityRegistry) : IAuthPacketProcessor<SubRootChanged>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;

    public async Task Process(AuthProcessorContext context, SubRootChanged packet)
    {
        // "No sub root" at the end of a respawn in the escape pod must not take the player out of it (PlayerParentRules)
        if (!PlayerParentRules.KeepsEscapePodParent(packet.SubRootId, PlayerParentRules.GetParent(entityRegistry, context.Sender.GameObjectId)))
        {
            entityRegistry.ReparentEntity(context.Sender.GameObjectId, packet.SubRootId.OrNull());
            context.Sender.SubRootId = packet.SubRootId;
        }
        await context.SendToOthersAsync(packet);
    }
}
