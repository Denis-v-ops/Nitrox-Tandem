using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Buddy breathing (COOP_FEATURES_SPEC T2): the target alone gets what the donor paid, clamped and scaled by
///     <see cref="SubnauticaServerOptions.BuddyBreathingEfficiency" />. Without this processor the default one would
///     relay the full amount to everyone.
/// </summary>
internal sealed class OxygenShareProcessor(PartnerInteractions partnerInteractions, IOptions<SubnauticaServerOptions> options, ILogger<OxygenShareProcessor> logger)
    : IAuthPacketProcessor<OxygenShare>
{
    private readonly PartnerInteractions partnerInteractions = partnerInteractions;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<OxygenShareProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, OxygenShare packet)
    {
        // A downed partner (T1) is the main reason to share air.
        PartnerInteractionResult result = partnerInteractions.Check(context.Sender, packet.TargetId, PartnerInteractionRules.TargetMayBeDowned, out Player? target);
        if (result != PartnerInteractionResult.Allowed || target == null)
        {
            logger.ZLogDebug($"Dropped oxygen from {context.Sender.Name} for session {packet.TargetId}: {result}");
            return;
        }
        float received = AmountReceived(packet.Amount, options.Value.BuddyBreathingEfficiency);
        if (received <= 0)
        {
            return;
        }
        await context.SendAsync(new OxygenShare(context.Sender.SessionId, target.SessionId, received), target.SessionId);
    }

    /// <summary>
    ///     What the receiver gets for <paramref name="paid" />: at most <see cref="OxygenShare.MAX_AMOUNT" /> per packet,
    ///     times the efficiency (0 to 1). Bad values give 0.
    /// </summary>
    internal static float AmountReceived(float paid, float efficiency)
    {
        if (!(paid > 0) || !(efficiency > 0))
        {
            return 0;
        }
        return Math.Min(paid, OxygenShare.MAX_AMOUNT) * Math.Min(efficiency, 1f);
    }
}
