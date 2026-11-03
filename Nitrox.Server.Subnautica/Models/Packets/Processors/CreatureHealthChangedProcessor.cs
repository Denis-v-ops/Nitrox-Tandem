using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Packets.Processors.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Shares the health of a creature reported by its simulating player with the players who can see it, who are the possible next simulating players.
/// </summary>
internal sealed class CreatureHealthChangedProcessor(SimulationOwnershipData simulationOwnershipData, PlayerManager playerManager, EntityRegistry entityRegistry)
    : TransmitIfCanSeePacketProcessor<CreatureHealthChanged>(playerManager, entityRegistry)
{
    /// <summary>
    ///     MULTINAUGHTICA_SPEC M5 (ported from MultiNaughtica): how long after losing the lock the previous simulating player's last report is still
    ///     accepted. Its report throttle (0.2 s) can hold the final health back past the hand-over, and that report carries the hits the new
    ///     simulating player doesn't know yet. The new simulating player's game takes it for 1 s (CreatureDamageRules.HAND_OVER_GRACE_TIME).
    /// </summary>
    internal const long PREVIOUS_OWNER_GRACE_MS = 1500;

    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;

    public override async Task Process(AuthProcessorContext context, CreatureHealthChanged packet)
    {
        Player? simulatingPlayer = simulationOwnershipData.GetPlayerForLock(packet.CreatureId);
        bool senderWasRecentOwner = simulationOwnershipData.WasOwnerWithin(packet.CreatureId, context.Sender.SessionId, PREVIOUS_OWNER_GRACE_MS);
        if (!IsReportFromSimulatingPlayer(packet, simulatingPlayer, context.Sender, senderWasRecentOwner))
        {
            return;
        }

        if (simulatingPlayer != null && simulatingPlayer.SessionId == context.Sender.SessionId)
        {
            // The simulating player has reported: whatever the previous one still has in flight is older than this
            simulationOwnershipData.ForgetPreviousOwner(packet.CreatureId);
        }

        // Also drops reports on dead creatures, which are no longer registered
        await TransmitIfCanSeeEntitiesAsync(context, packet, [packet.CreatureId]);
    }

    /// <summary>
    ///     Only the simulating player's health is real: this drops late reports of a previous simulating player, except the last one right after
    ///     the hand-over (<paramref name="senderWasRecentOwner" />), which the new simulating player has to continue from.
    /// </summary>
    internal static bool IsReportFromSimulatingPlayer(CreatureHealthChanged packet, Player? simulatingPlayer, Player sender, bool senderWasRecentOwner = false)
    {
        // Receivers write the value directly, deaths are replicated by RemoveCreatureCorpse and EntityDestroyed
        if (!(packet.Health > 0f) || float.IsInfinity(packet.Health))
        {
            return false;
        }

        // Compared by session: Player's == compares the PeerId
        return senderWasRecentOwner || (simulatingPlayer != null && simulatingPlayer.SessionId == sender.SessionId);
    }
}
