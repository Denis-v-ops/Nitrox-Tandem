using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using static Nitrox.Model.Subnautica.Packets.PlayerReviveRequest;

namespace NitroxClient.GameLogic.Partner;

/// <summary>
///     Reviving a downed partner (COOP_FEATURES_SPEC T1): hold the interact button on their body for 6 s, or 2 s with a
///     first aid kit in hand. The server checks it (<c>RescueService</c>), uses up the kit and sends
///     <see cref="PlayerRevived" />. Letting go or looking away resets the hold (<see cref="PartnerHandTarget" />).
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
        string key = TryGetHeldKit(out _) ? "Nitrox_ReviveWithKit" : "Nitrox_Revive";
        return Language.main.Get(key).Replace("{PLAYER}", partner.Partner.PlayerName);
    }

    public override string GetSubscript(PartnerHandTarget partner) => TryGetHeldKit(out _) ? string.Empty : Language.main.Get("Nitrox_ReviveHint");

    public override bool OnHeld(PartnerHandTarget partner, float heldSeconds, float deltaTime, out float progress)
    {
        bool kit = TryGetHeldKit(out NitroxId? kitId);
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
    ///     A first aid kit drawn from the quick slots (the game holds it as an item, <c>QuickSlots.DrawAsItem</c>).
    /// </summary>
    private static bool TryGetHeldKit(out NitroxId? kitId)
    {
        kitId = null;
        InventoryItem held = Inventory.main ? Inventory.main.quickSlots.heldItem : null;
        Pickupable item = held?.item;
        if (!item || item.GetTechType() != TechType.FirstAidKit || !item.TryGetNitroxId(out NitroxId id))
        {
            return false;
        }
        kitId = id;
        return true;
    }
}
