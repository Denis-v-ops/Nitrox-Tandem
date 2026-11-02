using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class PlayerHeldItemChangedProcessor : IAuthPacketProcessor<PlayerHeldItemChanged>
{
    public async Task Process(AuthProcessorContext context, PlayerHeldItemChanged packet)
    {
        if (packet.IsFirstTime != null && !context.Sender.UsedItems.Contains(packet.IsFirstTime))
        {
            context.Sender.UsedItems.Add(packet.IsFirstTime);
        }

        // Kept for players who join later (COOP_FEATURES_SPEC R2a)
        switch (packet.Type)
        {
            case PlayerHeldItemChanged.ChangeType.DRAW_AS_TOOL:
            case PlayerHeldItemChanged.ChangeType.DRAW_AS_ITEM:
                context.Sender.HeldItem = packet;
                break;
            case PlayerHeldItemChanged.ChangeType.HOLSTER_AS_TOOL:
            case PlayerHeldItemChanged.ChangeType.HOLSTER_AS_ITEM:
                if (context.Sender.HeldItem?.ItemId == packet.ItemId)
                {
                    context.Sender.HeldItem = null;
                }
                break;
        }

        await context.SendToOthersAsync(packet);
    }
}
