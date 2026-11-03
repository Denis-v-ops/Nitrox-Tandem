using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Crafting from lockers (MULTINAUGHTICA_SPEC M8): removes the items a crafting player claims from the lockers of a base or
///     a Cyclops, or says someone was faster. The removal is the one an <see cref="EntityDestroyed" /> does, announced to
///     everyone (the claimant's copy goes away with the partner's); the answer goes to the claimant after those packets, and
///     the next claim waits until they are out (<see cref="CraftingClaimArbiter.ClaimAsync" />). With
///     <c>CraftFromLockers</c> off the server refuses every claim, whatever the game thinks.
/// </summary>
internal sealed class CraftingClaimRequestProcessor(
    CraftingClaimArbiter craftingClaimArbiter,
    WorldEntityManager worldEntityManager,
    EntitySimulation entitySimulation,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CraftingClaimRequestProcessor> logger) : IAuthPacketProcessor<CraftingClaimRequest>
{
    private readonly CraftingClaimArbiter craftingClaimArbiter = craftingClaimArbiter;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CraftingClaimRequestProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CraftingClaimRequest packet)
    {
        if (!options.Value.CraftFromLockers)
        {
            await context.ReplyAsync(new CraftingClaimResponse(packet.ClaimId, CraftingClaimOutcome.Disabled, []));
            return;
        }

        await craftingClaimArbiter.ClaimAsync(context.Sender.SessionId, packet.ClaimId, packet.ScopeId, packet.ItemIds, Remove, async decision =>
        {
            if (decision.Outcome == CraftingClaimOutcome.Granted)
            {
                logger.ZLogInformation($"{context.Sender.Name} crafts from the lockers of {packet.ScopeId}: took {decision.Removed.Count} items ({string.Join(", ", decision.Removed.Select(entity => entity.TechType))})");
                foreach (Entity removed in decision.Removed)
                {
                    await context.SendToAllAsync(new EntityDestroyed(removed.Id));
                }
            }
            else
            {
                logger.ZLogInformation($"{context.Sender.Name} can't craft from the lockers of {packet.ScopeId}: {decision.Outcome}, {decision.Unavailable.Count} of {packet.ItemIds?.Count ?? 0} items unavailable");
            }

            await context.ReplyAsync(new CraftingClaimResponse(packet.ClaimId, decision.Outcome, [.. decision.Unavailable]));
        });
    }

    private Entity? Remove(NitroxId id)
    {
        if (!worldEntityManager.TryDestroyEntity(id, out Entity? entity))
        {
            return null;
        }
        entitySimulation.EntityDestroyed(id);
        return entity;
    }
}
