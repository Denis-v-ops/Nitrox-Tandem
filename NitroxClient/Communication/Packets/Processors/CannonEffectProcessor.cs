using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class CannonEffectProcessor : IClientPacketProcessor<CannonEffect>
{
    public Task Process(ClientProcessorContext context, CannonEffect packet)
    {
        CannonEffects.Apply(packet);
        return Task.CompletedTask;
    }
}
