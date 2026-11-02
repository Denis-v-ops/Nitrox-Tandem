using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using Story;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class RadioPlayPendingMessageProcessor : IClientPacketProcessor<RadioPlayPendingMessage>
{
    public Task Process(ClientProcessorContext context, RadioPlayPendingMessage packet)
    {
        // The "OnPlay" goal this completes is the partner's to report (StoryGoalManager_OnGoalComplete_Patch), and the server
        // records it from this packet already (MULTINAUGHTICA_SPEC M2)
        using (PacketSuppressor<StoryGoalExecuted>.Suppress())
        {
            StoryGoalManager.main.ExecutePendingRadioMessage();
        }
        return Task.CompletedTask;
    }
}
