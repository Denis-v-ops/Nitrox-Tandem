using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using static Nitrox.Model.Subnautica.Packets.PlayerReviveRequest;

namespace NitroxClient.GameLogic.Partner;

/// <summary>
///     Reviving a downed partner (COOP_FEATURES_SPEC T1): hold the interact button on their body for 6 s, or 2 s when the
///     rescuer carries a first aid kit. The kit can't be held in hand: it isn't a hand item (<c>InventoryItem.isBindable</c>
///     is false), so it never goes into a quick slot, and the game uses it from the inventory. The server checks it
///     (<c>RescueService</c>), uses up the kit and sends <see cref="PlayerRevived" />. Letting go or looking away resets the hold (<see cref="PartnerHandTarget" />).
/// </summary>
/// <remarks>
///     It outranks every other action: buddy breathing is offered to a downed partner too, but getting them up comes
///     first, and giving items (T3) isn't offered to a downed partner at all.
/// </remarks>
public sealed class ReviveAction(IPacketSender packetSender) : PartnerAction
{
    private readonly IPacketSender packetSender = packetSender;

    public override int Priority => 100;

    public override bool IsAvailable(PartnerHandTarget partner) => partner.IsDowned;

    public override string GetText(PartnerHandTarget partner)
    {
        string key = TryGetKit(out _) ? "Nitrox_ReviveWithKit" : "Nitrox_Revive";
        return Language.main.Get(key).Replace("{PLAYER}", partner.Partner.PlayerName);
    }

    public override string GetSubscript(PartnerHandTarget partner) => TryGetKit(out _) ? string.Empty : Language.main.Get("Nitrox_ReviveHint");

    public override bool OnHeld(PartnerHandTarget partner, float heldSeconds, float deltaTime, out float progress)
    {
        bool kit = TryGetKit(out NitroxId? kitId);
        float duration = DownedRules.ReviveSeconds(kit);
        progress = heldSeconds / duration;
        if (heldSeconds < duration)
        {
            return true;
        }
        progress = 1f;
        packetSender.Send(new PlayerReviveRequest(partner.Partner.SessionId, kit ? ReviveMethod.FIRST_AID_KIT : ReviveMethod.HAND, Optional.OfNullable(kitId)));
        Log.Info($"Asked to revive {partner.Partner.PlayerName} ({(kit ? "first aid kit" : "by hand")})");
        return false;
    }

    /// <summary>
    ///     A first aid kit in the local player's inventory.
    /// </summary>
    private static bool TryGetKit(out NitroxId? kitId)
    {
        kitId = null;
        if (!Inventory.main)
        {
            return false;
        }
        IList<InventoryItem> kits = Inventory.main.container.GetItems(TechType.FirstAidKit);
        if (kits == null)
        {
            return false;
        }
        foreach (InventoryItem kit in kits)
        {
            if (kit.item && kit.item.TryGetNitroxId(out NitroxId id))
            {
                kitId = id;
                return true;
            }
        }
        return false;
    }
}
