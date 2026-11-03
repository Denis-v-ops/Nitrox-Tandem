using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5): a partner let go of a Seamoth, or the server ended a hitch.
/// </summary>
internal sealed class PlayerUnhitchedProcessor(SeamothHitch seamothHitch) : IClientPacketProcessor<PlayerUnhitched>
{
    private readonly SeamothHitch seamothHitch = seamothHitch;

    public Task Process(ClientProcessorContext context, PlayerUnhitched packet)
    {
        seamothHitch.OnUnhitched(packet.PlayerId, packet.Position.ToUnity());
        return Task.CompletedTask;
    }
}
