using Nitrox.Model.GameLogic.PlayerAnimation;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
/// Broadcasts and stores the animation state of a player
/// </summary>
internal sealed class AnimationChangeEventProcessor : IAuthPacketProcessor<AnimationChangeEvent>
{
    public async Task Process(AuthProcessorContext context, AnimationChangeEvent packet)
    {
        // Only states are kept for players who join later, one per type (COOP_FEATURES_SPEC R2a). The infection reveal is a
        // one-time animation (the game sets a trigger): kept, it would play again on the partner's body for every late joiner.
        if (packet.Animation.Type != AnimChangeType.INFECTION_REVEAL)
        {
            context.Sender.PlayerContext.Animation = packet.Animation;
            context.Sender.Animations[packet.Animation.Type] = packet.Animation.State;
        }
        await context.SendToOthersAsync(packet);
    }
}
