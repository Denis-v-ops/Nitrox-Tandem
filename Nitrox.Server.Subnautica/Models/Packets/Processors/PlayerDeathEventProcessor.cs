using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class PlayerDeathEventProcessor(IOptions<SubnauticaServerOptions> config, RescueService rescueService) : IAuthPacketProcessor<PlayerDeathEvent>
{
    private readonly IOptions<SubnauticaServerOptions> options = config;
    private readonly RescueService rescueService = rescueService;

    public async Task Process(AuthProcessorContext context, PlayerDeathEvent packet)
    {
        if (options.Value.IsHardcore())
        {
            context.Sender.IsPermaDeath = true;
            await context.ReplyAsync(new PlayerKicked("Permanent death from hardcore mode"));
        }
        context.Sender.LastStoredPosition = packet.DeathPosition;
        context.Sender.LastStoredSubRootID = context.Sender.SubRootId;
        if (context.Sender.Permissions > Perms.MODERATOR)
        {
            await context.ReplyAsync(new ChatMessage(SessionId.SERVER_ID, "You can use /back to go to your death location"));
        }
        await context.SendToOthersAsync(packet);
        // Death ends a rescue window that was still open (COOP_FEATURES_SPEC T1)
        await rescueService.PlayerDiedAsync(context.Sender);
    }
}
