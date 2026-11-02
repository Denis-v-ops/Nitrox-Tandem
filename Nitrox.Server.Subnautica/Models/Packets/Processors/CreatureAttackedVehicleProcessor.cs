using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     COOP_FEATURES_SPEC D4: a vehicle's health changes only in the game that holds its lock (the pilot's), and a creature
///     attacks only in the game that simulates it. The bite goes from the creature's simulator to the vehicle's lock
///     holder, and from nobody else: a second game sending it would damage the vehicle twice.
/// </summary>
internal sealed class CreatureAttackedVehicleProcessor(SimulationOwnershipData simulationOwnershipData, ILogger<CreatureAttackedVehicleProcessor> logger)
    : IAuthPacketProcessor<CreatureAttackedVehicle>
{
    private readonly ILogger<CreatureAttackedVehicleProcessor> logger = logger;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;

    public async Task Process(AuthProcessorContext context, CreatureAttackedVehicle packet)
    {
        if (simulationOwnershipData.GetPlayerForLock(packet.CreatureId) != context.Sender)
        {
            logger.ZLogDebug($"Dropped an attack of creature {packet.CreatureId} on vehicle {packet.VehicleId} by {context.Sender.Name}, who doesn't simulate it");
            return;
        }
        Player vehicleOwner = simulationOwnershipData.GetPlayerForLock(packet.VehicleId);
        if (vehicleOwner == null || vehicleOwner == context.Sender)
        {
            return;
        }

        await context.SendAsync(packet, vehicleOwner.SessionId);
    }
}
