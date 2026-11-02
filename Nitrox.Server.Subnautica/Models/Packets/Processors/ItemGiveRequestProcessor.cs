using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Handing an item over (COOP_FEATURES_SPEC T3). The server moves the item from the giver's inventory into the
///     target's only if the two can reach each other (neither down) and the item is a plain inventory item of the giver,
///     not equipped. Then everyone gets <see cref="EntityReparented" />: the giver's game moves it into its copy of the
///     target's inventory, the target's game into its own. The item keeps its id and its children (a tool's battery).
///     A refused request changes nothing anywhere.
/// </summary>
/// <remarks>
///     The server has no item sizes, so whether the target has room is checked by the giver's game, on its copy of the
///     target's inventory (<c>GiveItemAction</c>).
/// </remarks>
internal sealed class ItemGiveRequestProcessor(PartnerInteractions partnerInteractions, EntityRegistry entityRegistry, ILogger<ItemGiveRequestProcessor> logger)
    : IAuthPacketProcessor<ItemGiveRequest>
{
    private readonly PartnerInteractions partnerInteractions = partnerInteractions;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<ItemGiveRequestProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, ItemGiveRequest packet)
    {
        Player sender = context.Sender;
        PartnerInteractionResult result = partnerInteractions.Check(sender, packet.TargetId, PartnerInteractionRules.None, out Player? target);
        if (result != PartnerInteractionResult.Allowed || target == null)
        {
            logger.ZLogDebug($"Refused giving {packet.ItemId} from {sender.Name} to session {packet.TargetId}: {result}");
            return;
        }
        if (!CanGive(sender, packet, out string? refusal))
        {
            logger.ZLogDebug($"Refused giving {packet.ItemId} from {sender.Name} to {target.Name}: {refusal}");
            return;
        }

        entityRegistry.ReparentEntity(packet.ItemId, target.GameObjectId);
        logger.ZLogInformation($"{sender.Name} gave {packet.ItemId} to {target.Name}");
        await context.SendToAllAsync(new EntityReparented(packet.ItemId, target.GameObjectId));
    }

    private bool CanGive(Player sender, ItemGiveRequest packet, out string? refusal)
    {
        if (!entityRegistry.TryGetEntityById(packet.ItemId, out Entity? item))
        {
            refusal = "unknown item";
            return false;
        }
        if (item is not InventoryItemEntity)
        {
            refusal = $"not an inventory item ({item.GetType().Name})";
            return false;
        }
        if (item.ParentId != sender.GameObjectId)
        {
            refusal = $"not in the giver's inventory (parent {item.ParentId})";
            return false;
        }
        if (sender.EquippedItems.Values.Contains(packet.ItemId))
        {
            refusal = "equipped";
            return false;
        }
        refusal = null;
        return true;
    }
}
