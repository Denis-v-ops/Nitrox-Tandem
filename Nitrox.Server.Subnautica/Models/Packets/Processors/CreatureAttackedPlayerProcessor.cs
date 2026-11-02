using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     FIX_SPEC S14: only the game that simulates a creature runs its attacks, so only that player may hurt someone with
///     it. Anything else (a lost race over the lock, a stale packet) would hurt the player a second time.
/// </summary>
internal sealed class CreatureAttackedPlayerProcessor(SimulationOwnershipData simulationOwnershipData, PlayerManager playerManager, ILogger<CreatureAttackedPlayerProcessor> logger)
    : IAuthPacketProcessor<CreatureAttackedPlayer>
{
    private readonly ILogger<CreatureAttackedPlayerProcessor> logger = logger;
    private readonly PlayerManager playerManager = playerManager;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;

    public async Task Process(AuthProcessorContext context, CreatureAttackedPlayer packet)
    {
        if (simulationOwnershipData.GetPlayerForLock(packet.CreatureId) != context.Sender)
        {
            logger.ZLogDebug($"Dropped an attack of creature {packet.CreatureId} by {context.Sender.Name}, who doesn't simulate it");
            return;
        }
        if (packet.TargetSessionId == context.Sender.SessionId || !playerManager.TryGetPlayerBySessionId(packet.TargetSessionId, out Player target))
        {
            return;
        }

        await context.SendAsync(packet, target.SessionId);
    }
}
