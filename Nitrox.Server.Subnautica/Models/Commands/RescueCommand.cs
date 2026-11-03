using System.ComponentModel;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;

namespace Nitrox.Server.Subnautica.Models.Commands;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): who is down and how the escalation stands, and a rest for a player as a
///     completed sleep would give them.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class RescueCommand(RescueService rescueService) : ICommandHandler<RescueCommand.RescueAction>, ICommandHandler<RescueCommand.RescueAction, Player>
{
    private readonly RescueService rescueService = rescueService;

    [Description("Shows the rescue window: who is down, the time left, and each player's downs since they rested")]
    public async Task Execute(ICommandContext context, [Description("status")] RescueAction action)
    {
        if (action != RescueAction.STATUS)
        {
            await context.ReplyAsync("Name the player, e.g. \"rescue rest Alice\"");
            return;
        }
        await context.ReplyAsync(string.Join(Environment.NewLine, rescueService.Describe()));
    }

    [Description("Gives a player a rest, as a completed sleep does: their next rescue window is the full one again")]
    public async Task Execute(ICommandContext context, [Description("rest")] RescueAction action, [Description("The player")] Player player)
    {
        if (action != RescueAction.REST)
        {
            await Execute(context, action);
            return;
        }
        player.DownsSinceRest = 0;
        await context.ReplyAsync($"{player.Name} rested: their next rescue window is the full one");
    }

    public enum RescueAction
    {
        STATUS,
        REST
    }
}
