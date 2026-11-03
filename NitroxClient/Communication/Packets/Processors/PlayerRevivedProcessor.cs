using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): someone was revived, this player or a partner.
/// </summary>
internal sealed class PlayerRevivedProcessor(RescueWindow rescueWindow) : IClientPacketProcessor<PlayerRevived>
{
    private readonly RescueWindow rescueWindow = rescueWindow;

    public Task Process(ClientProcessorContext context, PlayerRevived packet)
    {
        rescueWindow.OnRevived(packet.PlayerId, packet.Health);
        return Task.CompletedTask;
    }
}
