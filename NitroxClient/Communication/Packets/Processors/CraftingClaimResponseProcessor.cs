using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     The server's answer to a claim for crafting from lockers (MULTINAUGHTICA_SPEC M8): a granted answer starts the craft,
///     any other cancels or retries it (<see cref="LockerCrafting.OnResponse" />). It arrives after the
///     <see cref="EntityDestroyed" /> packets of the claimed items.
/// </summary>
internal sealed class CraftingClaimResponseProcessor(LockerCrafting lockerCrafting) : IClientPacketProcessor<CraftingClaimResponse>
{
    private readonly LockerCrafting lockerCrafting = lockerCrafting;

    public Task Process(ClientProcessorContext context, CraftingClaimResponse packet)
    {
        lockerCrafting.OnResponse(packet);
        return Task.CompletedTask;
    }
}
