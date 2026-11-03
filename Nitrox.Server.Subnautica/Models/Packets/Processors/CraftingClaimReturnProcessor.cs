using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Crafting from lockers (MULTINAUGHTICA_SPEC M8): a granted claim whose craft couldn't start. The items go back to the
///     lockers they were taken from (<see cref="CraftingClaimArbiter.Return" />) and every game spawns them there again.
/// </summary>
internal sealed class CraftingClaimReturnProcessor(CraftingClaimArbiter craftingClaimArbiter, ILogger<CraftingClaimReturnProcessor> logger) : IAuthPacketProcessor<CraftingClaimReturn>
{
    private readonly CraftingClaimArbiter craftingClaimArbiter = craftingClaimArbiter;
    private readonly ILogger<CraftingClaimReturnProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CraftingClaimReturn packet)
    {
        List<Entity> returned = craftingClaimArbiter.Return(context.Sender.SessionId, packet.ClaimId, context.Sender.GameObjectId);
        if (returned.Count == 0)
        {
            logger.ZLogWarning($"{context.Sender.Name} returns crafting claim {packet.ClaimId}, which isn't a granted claim the server knows");
            return;
        }

        logger.ZLogInformation($"{context.Sender.Name} returns crafting claim {packet.ClaimId}: {returned.Count} items go back");
        await context.SendToAllAsync(new SpawnEntities(returned, [], true));
    }
}
