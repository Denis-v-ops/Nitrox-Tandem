using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     FIX_SPEC S15d: another game's bleeder latched onto a player or let go (see <see cref="Bleeders" />).
/// </summary>
internal sealed class BleederAttachChangedProcessor(Bleeders bleeders) : IClientPacketProcessor<BleederAttachChanged>
{
    private readonly Bleeders bleeders = bleeders;

    public Task Process(ClientProcessorContext context, BleederAttachChanged packet)
    {
        bleeders.Apply(packet);
        return Task.CompletedTask;
    }
}
