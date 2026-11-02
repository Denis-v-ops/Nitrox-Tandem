using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
/// Broadcasts and stores the animation state of a player
/// </summary>
internal sealed class AnimationChangeEventProcessor : IAuthPacketProcessor<AnimationChangeEvent>
{
    public async Task Process(AuthProcessorContext context, AnimationChangeEvent packet)
    {
        context.Sender.PlayerContext.Animation = packet.Animation;
        // One state per type, so a later change of another type doesn't hide this one from late joiners (COOP_FEATURES_SPEC R2a)
        context.Sender.Animations[packet.Animation.Type] = packet.Animation.State;
        await context.SendToOthersAsync(packet);
    }
}
