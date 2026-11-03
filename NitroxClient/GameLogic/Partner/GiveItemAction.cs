using System.Diagnostics.CodeAnalysis;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Partner;

/// <summary>
///     Handing items over (COOP_FEATURES_SPEC T3): press the interact button on the partner to open the game's two-panel
///     transfer screen with your inventory on one side and your copy of the partner's inventory on the other, as a
///     storage locker or the bioreactor does (<c>BaseBioReactor.OnUse</c>). Moving an item over (click or drag) only
///     asks the server (<see cref="ItemGiveRequest" />); nothing moves until it agrees, then
///     <c>EntityReparentedProcessor</c> moves the item on every game. Nothing can be taken out of the partner's side.
/// </summary>
/// <remarks>
///     The price (§2.2): nothing equipped can be given, and the partner needs room. It allows nothing that dropping the
///     item and the partner picking it up didn't already.
/// </remarks>
public sealed class GiveItemAction(IPacketSender packetSender, Entities entities) : PartnerAction
{
    /// <summary>
    ///     The transfer screen closes when the partner is farther than this. The server allows a give up to
    ///     <c>PartnerInteractions.MAX_DISTANCE</c> (4 m) by its lagging positions.
    /// </summary>
    public const float MAX_SCREEN_DISTANCE = 3f;

    private readonly IPacketSender packetSender = packetSender;
    private readonly Entities entities = entities;

    /// <summary>
    ///     Below buddy breathing (and later revives): an emergency action wins when both apply.
    /// </summary>
    public override int Priority => -10;

    /// <summary>
    ///     Not while the partner pilots the Cyclops or sits in a vehicle: a pilot can't take anything, and a crew member
    ///     behind the helm aims past the pilot's body at the helm screen (COOP_FEATURES_SPEC T4). Offering a give there
    ///     opened the PDA on every click meant for the screen.
    /// </summary>
    public override bool IsAvailable(PartnerHandTarget partner) => !partner.IsDowned && !partner.Partner.PilotingChair && !partner.Partner.Vehicle;

    public override string GetText(PartnerHandTarget partner)
    {
        return Language.main.Get("Nitrox_GiveItem").Replace("{PLAYER}", partner.Partner.PlayerName);
    }

    public override bool OnHeld(PartnerHandTarget partner, float heldSeconds, float deltaTime, out float progress)
    {
        progress = 0;
        RemotePlayer remotePlayer = partner.Partner;
        ItemsContainer container = remotePlayer.Inventory;
        container.isAllowedToRemove = static (_, _) => false;
        container.isAllowedToAdd = (pickupable, verbose) => RequestGive(partner, pickupable, verbose);
        container._label = Language.main.Get("Nitrox_GiveItemStorageLabel").Replace("{PLAYER}", remotePlayer.PlayerName);

        // As BaseBioReactor.OnUse. PDA.Open puts the held item away first (Inventory.ReturnHeld) and closes again when the
        // target is gone (the partner left) or out of range.
        PDA pda = Player.main.GetPDA();
        Inventory.main.SetUsedStorage(container);
        if (!pda.Open(PDATab.Inventory, partner.transform))
        {
            Inventory.main.ClearUsedStorage();
            return false;
        }
        pda.activeSqrDistance = Mathf.Max(pda.activeSqrDistance, MAX_SCREEN_DISTANCE * MAX_SCREEN_DISTANCE);
        return false;
    }

    /// <summary>
    ///     The partner's container copy's <see cref="ItemsContainer.isAllowedToAdd" />. The game asks with
    ///     <paramref name="verbose" /> false to offer the move (<c>Inventory.GetAllItemActions</c>), and with true when it
    ///     moves the item (<c>ItemsContainer.AddItem</c>): then the request goes to the server and the local move is
    ///     refused, so the item stays where it is until the server's <see cref="EntityReparented" /> arrives.
    /// </summary>
    private bool RequestGive(PartnerHandTarget partner, Pickupable pickupable, bool verbose)
    {
        if (!CanGive(partner, pickupable, out NitroxId? itemId, out string? refusal))
        {
            if (verbose && refusal != null)
            {
                ErrorMessage.AddMessage(refusal);
            }
            return false;
        }
        if (!verbose)
        {
            return true;
        }
        packetSender.Send(new ItemGiveRequest(itemId, partner.Partner.SessionId));
        Log.Debug($"Asked to give {pickupable.GetTechType()} ({itemId}) to {partner.Partner.PlayerName}");
        return false;
    }

    private bool CanGive(PartnerHandTarget partner, Pickupable pickupable, [NotNullWhen(true)] out NitroxId? itemId, out string? refusal)
    {
        itemId = null;
        refusal = null;
        if (!pickupable || partner.IsDowned)
        {
            return false;
        }
        IItemsContainer? from = pickupable.inventoryItem?.container;
        if (from != Inventory.main.container)
        {
            // Dragged from an equipment slot (or anything else that isn't the inventory)
            if (from == Inventory.main.equipment)
            {
                refusal = Language.main.Get("Nitrox_GiveItemEquipped");
            }
            return false;
        }
        // Only an item this game knows as an inventory item moves out of the inventory when the server's reparent
        // arrives (EntityReparentedProcessor); the server refuses anything else as well.
        if (!pickupable.TryGetNitroxId(out itemId) || !entities.IsKnownEntity(itemId) || entities.RequireEntityType(itemId) != typeof(InventoryItemEntity))
        {
            return false;
        }
        // The copy holds the same items as the partner's inventory and packs them the same way, so it has room exactly
        // when the partner has. It also holds what the partner wears, so it errs on the side of "no room".
        if (!partner.Partner.Inventory.HasRoomFor(pickupable))
        {
            refusal = Language.main.Get("Nitrox_GiveItemNoRoom").Replace("{PLAYER}", partner.Partner.PlayerName);
            return false;
        }
        return true;
    }
}
