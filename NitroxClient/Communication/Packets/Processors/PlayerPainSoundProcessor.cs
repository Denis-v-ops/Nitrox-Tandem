using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class PlayerPainSoundProcessor(PlayerManager playerManager) : IClientPacketProcessor<PlayerPainSound>
{
    private readonly PlayerManager playerManager = playerManager;

    public Task Process(ClientProcessorContext context, PlayerPainSound packet)
    {
        if (playerManager.TryFind(packet.SessionId, out RemotePlayer remotePlayer) && remotePlayer.Body &&
            remotePlayer.Body.TryGetComponent(out RemotePlayerSounds sounds))
        {
            sounds.PlayPain(packet.Kind);
        }
        return Task.CompletedTask;
    }
}
