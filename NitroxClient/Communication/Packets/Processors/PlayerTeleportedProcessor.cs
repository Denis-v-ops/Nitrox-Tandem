using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;
using UWE;
using Terrain = NitroxClient.GameLogic.Terrain;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class PlayerTeleportedProcessor : IClientPacketProcessor<PlayerTeleported>
{
    public Task Process(ClientProcessorContext context, PlayerTeleported packet)
    {
        Player.main.OnPlayerPositionCheat();

        Vehicle currentVehicle = Player.main.currentMountedVehicle;
        if (currentVehicle)
        {
            currentVehicle.TeleportVehicle(packet.DestinationTo.ToUnity(), currentVehicle.transform.rotation);
            Player.main.WaitForTeleportation();
            return Task.CompletedTask;
        }

        Player.main.SetPosition(packet.DestinationTo.ToUnity());
        // OnPlayerPositionCheat cleared it, and a teleport doesn't pass the trigger volumes that set it (FIX_SPEC S6).
        Player.main.precursorOutOfWater = packet.InPrecursor;

        if (packet.SubRootID.HasValue && NitroxEntity.TryGetComponentFrom(packet.SubRootID.Value, out SubRoot subRoot))
        {
            Player.main.SetCurrentSub(subRoot, true);
            return Task.CompletedTask;
        }

        // Freeze the player while it's loading its new position
        Player.main.cinematicModeActive = true;
        Player.main.WaitForTeleportation();

        CoroutineHost.StartCoroutine(Terrain.SafeWaitForWorldLoad());
        return Task.CompletedTask;
    }
}
