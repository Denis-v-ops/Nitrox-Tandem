using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.Unity;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     Checks that one player may act on another up close (COOP_FEATURES_SPEC T2): buddy breathing, and later revives
///     (T1) and handing items over (T3).
/// </summary>
internal sealed class PartnerInteractions(PlayerManager playerManager)
{
    /// <summary>
    ///     The game lets a player target the partner's body from at most 2 m (camera to the body's collider,
    ///     <c>GUIHand.UpdateActiveTarget</c>). The server measures between the two players' last reported positions, which
    ///     lag behind (movement packets are unreliable and sent while moving) and sit at the body's center, so it allows
    ///     some slack on top.
    /// </summary>
    public const float MAX_DISTANCE = 4f;

    private readonly PlayerManager playerManager = playerManager;

    /// <summary>
    ///     Finds the online target by its session and checks the pair. <paramref name="target" /> is set whenever the
    ///     target is online, even if the check fails.
    /// </summary>
    public PartnerInteractionResult Check(Player sender, SessionId targetId, PartnerInteractionRules rules, out Player? target)
    {
        if (!playerManager.TryGetPlayerBySessionId(targetId, out target) || !target.IsOnline)
        {
            target = null;
            return PartnerInteractionResult.TargetOffline;
        }
        return Check(sender, target, rules);
    }

    public static PartnerInteractionResult Check(Player sender, Player? target, PartnerInteractionRules rules)
    {
        if (!sender.IsOnline)
        {
            return PartnerInteractionResult.SenderOffline;
        }
        if (target is not { IsOnline: true })
        {
            return PartnerInteractionResult.TargetOffline;
        }
        if (target.SessionId == sender.SessionId)
        {
            return PartnerInteractionResult.SameAsSender;
        }
        if (sender.DownedUntil.HasValue && !rules.HasFlag(PartnerInteractionRules.SenderMayBeDowned))
        {
            return PartnerInteractionResult.SenderDowned;
        }
        if (target.DownedUntil.HasValue && !rules.HasFlag(PartnerInteractionRules.TargetMayBeDowned))
        {
            return PartnerInteractionResult.TargetDowned;
        }
        if (!(NitroxVector3.Distance(sender.Position, target.Position) <= MAX_DISTANCE))
        {
            return PartnerInteractionResult.TooFar;
        }
        return PartnerInteractionResult.Allowed;
    }
}

[Flags]
internal enum PartnerInteractionRules
{
    None = 0,
    SenderMayBeDowned = 1,
    TargetMayBeDowned = 2
}

internal enum PartnerInteractionResult
{
    Allowed,
    SenderOffline,
    TargetOffline,
    SameAsSender,
    SenderDowned,
    TargetDowned,
    TooFar
}
