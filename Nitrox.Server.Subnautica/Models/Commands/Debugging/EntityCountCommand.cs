#if DEBUG
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Counts the entities the server holds, per tech type (entity type when it has none). Run it before and after leaving
///     and re-entering an area, killing creatures or restarting to see whether anything multiplies (upstream #2800).
///     Ported from MultiNaughtica.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class EntityCountCommand(EntityRegistry entityRegistry, ILogger<EntityCountCommand> logger) : ICommandHandler<string>
{
    private const int MAX_GROUPS = 25;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<EntityCountCommand> logger = logger;

    [Description("Counts the server's entities per tech type, optionally only the types whose name contains the filter")]
    public async Task Execute(ICommandContext context, [Description("Part of a tech type or entity type name")] string filter = "")
    {
        List<Entity> all = entityRegistry.GetAllEntities();
        IEnumerable<IGrouping<string, Entity>> groups = all.GroupBy(entity => entity.TechType is { } techType && techType.Name != "None" ? techType.Name : entity.GetType().Name)
                                                           .Where(group => filter == "" || group.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                                           .OrderByDescending(group => group.Count());
        List<string> lines = groups.Take(MAX_GROUPS).Select(group => $"{group.Key}: {group.Count()}").ToList();
        string summary = $"[EntityCount] {all.Count} entities{(filter == "" ? "" : $", filter '{filter}'")}: {string.Join(", ", lines)}";
        logger.ZLogInformation($"{summary}");
        await context.ReplyAsync(summary);
    }
}
#endif
