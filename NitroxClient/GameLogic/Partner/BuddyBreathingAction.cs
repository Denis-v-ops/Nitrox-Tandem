using Nitrox.Model.GameLogic.FMOD;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Partner;

/// <summary>
///     Buddy breathing (COOP_FEATURES_SPEC T2): hold the interact button on a partner who is low on air (or down) to give
///     them air from your tank. Slow and lossy: every <see cref="TICK_SECONDS" /> the donor pays
///     <see cref="OXYGEN_PER_TICK" />, and the server passes on only <c>BuddyBreathingEfficiency</c> (default half) of it.
/// </summary>
/// <remarks>
///     Oxygen is in seconds of air at shallow depth. Without a Rebreather a player breathes 1 unit/s in the shallows,
///     1.5/s below 100 m and 2/s below 200 m (<c>Player.GetBreathPeriod</c>, <c>Player.GetOxygenPerBreath</c>). With
///     2 units/s paid and 1 unit/s received at the default efficiency, buddy breathing halves the partner's drowning at
///     300 m and only holds their air level in the shallows: it buys time to reach air, it doesn't refill.
/// </remarks>
public sealed class BuddyBreathingAction(IPacketSender packetSender, FMODWhitelist fmodWhitelist) : PartnerAction
{
    public const float TICK_SECONDS = 0.5f;
    public const float OXYGEN_PER_TICK = OxygenShare.MAX_AMOUNT;

    /// <summary>
    ///     The partner's air below which they can be given air.
    /// </summary>
    public const float LOW_OXYGEN_FRACTION = 0.3f;

    /// <summary>
    ///     The donor stops when only this many seconds of their own air (at their current depth) would be left.
    /// </summary>
    public const float RESERVE_SECONDS = 15f;

    private const string BUBBLES_FALLBACK_PATH = "event:/player/bubbles";

    private readonly IPacketSender packetSender = packetSender;
    private readonly FMODWhitelist fmodWhitelist = fmodWhitelist;

    public override bool IsAvailable(PartnerHandTarget partner)
    {
        return partner.IsDowned || (partner.MaxOxygen > 0 && partner.Oxygen < partner.MaxOxygen * LOW_OXYGEN_FRACTION);
    }

    public override string GetText(PartnerHandTarget partner)
    {
        return Language.main.Get("Nitrox_BuddyBreathing").Replace("{PLAYER}", partner.Partner.PlayerName);
    }

    public override string GetSubscript(PartnerHandTarget partner)
    {
        OxygenManager oxygenManager = Player.main.oxygenMgr;
        if (!oxygenManager.HasOxygenTank())
        {
            return Language.main.Get("Nitrox_BuddyBreathingNeedTank");
        }
        if (GetGivableOxygen(oxygenManager) <= 0)
        {
            return Language.main.Get("Nitrox_BuddyBreathingReserve");
        }
        return string.Empty;
    }

    public override bool OnHeld(PartnerHandTarget partner, float heldSeconds, float deltaTime, out float progress)
    {
        OxygenManager oxygenManager = Player.main.oxygenMgr;
        float givable = oxygenManager.HasOxygenTank() ? GetGivableOxygen(oxygenManager) : 0;
        // The bar shows how much air the donor can still give.
        float givableCapacity = oxygenManager.GetOxygenCapacity() - GetReserve();
        progress = givableCapacity > 0 ? givable / givableCapacity : 0;
        if (givable <= 0)
        {
            return false;
        }
        if (!IntervalElapsed(heldSeconds, deltaTime, TICK_SECONDS))
        {
            return true;
        }

        float paid = oxygenManager.RemoveOxygen(Mathf.Min(OXYGEN_PER_TICK, givable));
        if (paid > 0)
        {
            packetSender.Send(new OxygenShare(default, partner.Partner.SessionId, paid));
            PlayBubbles(fmodWhitelist, partner.transform.position, partner.gameObject);
        }
        return true;
    }

    /// <summary>
    ///     Oxygen above the donor's reserve.
    /// </summary>
    private static float GetGivableOxygen(OxygenManager oxygenManager) => oxygenManager.GetOxygenAvailable() - GetReserve();

    /// <summary>
    ///     <see cref="RESERVE_SECONDS" /> of the local player's air at the current depth, at least that many units.
    /// </summary>
    private static float GetReserve()
    {
        Player player = Player.main;
        float breathPeriod = player.GetBreathPeriod();
        float perSecond = breathPeriod > 0 ? player.GetOxygenPerBreath(breathPeriod, player.depthClass.value) / breathPeriod : 0;
        return RESERVE_SECONDS * Mathf.Max(1f, perSecond);
    }

    /// <summary>
    ///     The player's breath bubbles at the partner's body, heard by the donor only (the receiver hears them on its own
    ///     side, <c>OxygenShareProcessor</c>). Played locally only: the one-shot doesn't go through Nitrox's sound sync.
    /// </summary>
    public static void PlayBubbles(FMODWhitelist fmodWhitelist, Vector3 position, GameObject? owner)
    {
        PlayerBreathBubbles bubbles = Player.main.GetComponentInChildren<PlayerBreathBubbles>(true);
        string path = bubbles && bubbles.bubbleSound && bubbles.bubbleSound.asset ? bubbles.bubbleSound.asset.path : BUBBLES_FALLBACK_PATH;
        if (!fmodWhitelist.IsWhitelisted(path, out float radius))
        {
            radius = 20f;
        }
        FMODEmitterController.PlayEventOneShot(path, radius, position);
#if DEBUG
        Debuggers.Bridge.BridgeSounds.Record("BuddyBreathing", path, position, owner);
#endif
    }
}
