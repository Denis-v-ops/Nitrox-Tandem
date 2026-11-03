using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): the server granted a window, to this player or a partner (D3 distress).
/// </summary>
internal sealed class PlayerDownedProcessor(RescueWindow rescueWindow) : IClientPacketProcessor<PlayerDowned>
{
    private readonly RescueWindow rescueWindow = rescueWindow;

    public Task Process(ClientProcessorContext context, PlayerDowned packet)
    {
        rescueWindow.OnDowned(packet.PlayerId, packet.WindowSeconds);
        return Task.CompletedTask;
    }
}
