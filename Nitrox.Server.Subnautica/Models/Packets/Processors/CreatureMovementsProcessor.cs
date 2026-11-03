using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     FIX_SPEC S16b/c: creature poses from their simulating game. Like <see cref="EntityTransformUpdatesProcessor" />: only
///     the lock holder's poses count, the record (and its cell) follows, so a player who arrives later spawns the creature
///     where it is; each other player gets the poses of the creatures they can see; and a creature that left its
///     simulator's loaded cells goes to a player who has it loaded (S16).
/// </summary>
internal sealed class CreatureMovementsProcessor(PlayerManager playerManager, WorldEntityManager worldEntityManager, SimulationOwnershipData simulationOwnershipData, EntitySimulation entitySimulation)
    : IAuthPacketProcessor<CreatureMovements>
{
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly PlayerManager playerManager = playerManager;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;

    public async Task Process(AuthProcessorContext context, CreatureMovements packet)
    {
        Dictionary<Player, List<CreatureMovement>> visibleByPlayer = [];
        foreach (Player player in playerManager.GetConnectedPlayers())
        {
            if (!player.Equals(context.Sender))
            {
                visibleByPlayer[player] = [];
            }
        }
        List<SimulatedEntity> handOvers = [];

        foreach (CreatureMovement movement in packet.Data)
        {
            // A pose sent just before the lock moved to someone else
            if (!simulationOwnershipData.TryGetLock(movement.Id, out SimulationOwnershipData.PlayerLock playerLock) || playerLock.Player != context.Sender)
            {
                continue;
            }
            if (!worldEntityManager.TryUpdateEntityPosition(movement.Id, movement.Position, movement.Rotation, out AbsoluteEntityCell _, out WorldEntity worldEntity))
            {
                continue;
            }
            foreach (KeyValuePair<Player, List<CreatureMovement>> playerMovements in visibleByPlayer)
            {
                if (playerMovements.Key.CanSee(worldEntity))
                {
                    playerMovements.Value.Add(movement);
                }
            }
            if (entitySimulation.TryHandOverFromBlindOwner(worldEntity, context.Sender, out SimulatedEntity? handOver))
            {
                handOvers.Add(handOver);
            }
        }

        foreach (KeyValuePair<Player, List<CreatureMovement>> playerMovements in visibleByPlayer)
        {
            if (playerMovements.Value.Count > 0)
            {
                await context.SendAsync(new CreatureMovements(playerMovements.Value, packet.RealTime), playerMovements.Key.SessionId);
            }
        }
        if (handOvers.Count > 0)
        {
            await context.SendToAllAsync(new SimulationOwnershipChange(handOvers));
        }
    }
}
