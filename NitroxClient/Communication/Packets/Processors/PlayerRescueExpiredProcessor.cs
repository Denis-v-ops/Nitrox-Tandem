using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1) ended without a revive: this player dies, or a partner's distress call
///     ends (D3).
/// </summary>
internal sealed class PlayerRescueExpiredProcessor(RescueWindow rescueWindow) : IClientPacketProcessor<PlayerRescueExpired>
{
    private readonly RescueWindow rescueWindow = rescueWindow;

    public Task Process(ClientProcessorContext context, PlayerRescueExpired packet)
    {
        rescueWindow.OnExpired(packet.PlayerId);
        return Task.CompletedTask;
    }
}
