using System.ComponentModel;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;

namespace Nitrox.Server.Subnautica.Models.Commands;

/// <summary>
///     Story gates (COOP_FEATURES_SPEC G0): see what the gates wait for, and let one through when a player can't come back.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class StoryGateCommand(StoryGateService storyGates) : ICommandHandler<StoryGateCommand.StoryGateAction>, ICommandHandler<StoryGateCommand.StoryGateAction, StoryGateKind>
{
    private readonly StoryGateService storyGates = storyGates;

    [Description("Shows the story gates: roster, who is online, what each gate waits for")]
    public async Task Execute(ICommandContext context, [Description("status")] StoryGateAction action)
    {
        if (action != StoryGateAction.STATUS)
        {
            await context.ReplyAsync("Name the gate to release, e.g. \"storygate release drive_core\"");
            return;
        }
        await context.ReplyAsync(string.Join(Environment.NewLine, storyGates.DescribeGates()));
    }

    [Description("Releases a story gate: its moment starts now, or the next time someone asks, without waiting for the others")]
    public async Task Execute(ICommandContext context, [Description("release")] StoryGateAction action, [Description("The gate to release")] StoryGateKind kind)
    {
        if (action != StoryGateAction.RELEASE)
        {
            await Execute(context, action);
            return;
        }
        bool started = await storyGates.ReleaseAsync(kind);
        await context.SendToAllAsync(started
                                         ? $"{context.OriginName} released the story gate {kind}: it starts now"
                                         : $"{context.OriginName} released the story gate {kind}: it starts the next time someone triggers it");
    }

    public enum StoryGateAction
    {
        STATUS,
        RELEASE
    }
}
