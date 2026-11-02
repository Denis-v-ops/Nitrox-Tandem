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
        // Only states are kept for players who join later, one per type (COOP_FEATURES_SPEC R2a, R2b). The infection reveal
        // and the fall states are relayed live only (IsReplayedToLateJoiners). The join payload's pose stays the body pose
        // (swimming or not); the arm states reach a late joiner through the replay after their initial sync.
        AnimChangeType type = packet.Animation.Type;
        if (type == AnimChangeType.UNDERWATER)
        {
            context.Sender.PlayerContext.Animation = packet.Animation;
        }
        if (type.IsReplayedToLateJoiners())
        {
            context.Sender.Animations[type] = packet.Animation.State;
        }
        await context.SendToOthersAsync(packet);
    }
}
