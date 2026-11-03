using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5): the server let this player or a partner hold on to a Seamoth.
/// </summary>
internal sealed class PlayerHitchedProcessor(SeamothHitch seamothHitch) : IClientPacketProcessor<PlayerHitched>
{
    private readonly SeamothHitch seamothHitch = seamothHitch;

    public Task Process(ClientProcessorContext context, PlayerHitched packet)
    {
        seamothHitch.OnHitched(packet.PlayerId, packet.VehicleId);
        return Task.CompletedTask;
    }
}
