#if DEBUG
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.DevBridge;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Logging.Scopes;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Services;

/// <summary>
///     Debug-only localhost endpoint for <c>tools/bridge.ps1 server ...</c>: runs server console commands and returns
///     their output, and dumps the server's entity records for <c>bridge.ps1 diff</c>. See <see cref="BridgeListener" />.
/// </summary>
internal sealed class DevBridgeService(CommandService commandService, IPacketSender packetSender, EntityRegistry entityRegistry, PlayerManager playerManager, ILogger<DevBridgeService> logger) : BackgroundService
{
    public const string BRIDGE_NAME = "server";

    private readonly CommandService commandService = commandService;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<DevBridgeService> logger = logger;
    private readonly IPacketSender packetSender = packetSender;
    private readonly PlayerManager playerManager = playerManager;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using BridgeListener listener = new(BRIDGE_NAME, HandleAsync);
        logger.ZLogInformation($"Dev bridge listening on 127.0.0.1:{listener.Port} (tools\\bridge.ps1 server help)");
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Server is stopping.
        }
    }

    private async Task<string> HandleAsync(string request)
    {
        List<string> args = BridgeText.SplitArgs(request);
        switch (args.FirstOrDefault()?.ToLowerInvariant())
        {
            case null:
            case "help":
                return """
                       Server dev bridge. Anything that isn't one of these is run as a server console command
                       (query <id>, save, restart, warp <player> <x> <y> <z>, ...) and its output is returned.
                         entities   dump all entity records (tab separated, see BridgeText.ENTITY_DUMP_HEADER)
                         players    known players: name, online, entity id, position
                       """;
            case "entities":
                return DumpEntities();
            case "players":
                return BridgeJson.Serialize(playerManager.GetAllPlayers().Select(p => new Dictionary<string, object?>
                {
                    ["name"] = p.Name,
                    ["online"] = p.IsOnline,
                    ["id"] = p.GameObjectId.ToString(),
                    ["position"] = new[] { p.Position.X, p.Position.Y, p.Position.Z },
                    ["subRootId"] = p.SubRootId.OrNull()?.ToString()
                }).ToList());
            default:
                return await RunCommandAsync(request);
        }
    }

    /// <summary>
    ///     Same as a player's <c>/command</c> (<see cref="Models.Packets.Processors.ServerCommandProcessor" />), but as the host.
    /// </summary>
    private async Task<string> RunCommandAsync(string command)
    {
        logger.ZLogInformation($"Dev bridge runs command '{command}'");
        string output;
        bool success;
        using (logger.BeginPlainScope())
        using (CaptureScope scope = logger.BeginCaptureScope())
        {
            success = commandService.ExecuteCommand(command, new HostToServerCommandContext(packetSender), out Task<bool>? task);
            if (task != null)
            {
                success = await task && success;
            }
            output = string.Join("", scope.Logs).TrimEnd('\r', '\n');
        }
        if (!string.IsNullOrWhiteSpace(output))
        {
            logger.ZLogInformation($"{output}");
        }
        return success ? output : $"{BridgeListener.ERROR_PREFIX}{(output == "" ? $"command '{command}' failed" : output)}";
    }

    private string DumpEntities()
    {
        StringBuilder builder = new();
        builder.AppendLine(BridgeText.ENTITY_DUMP_HEADER);
        foreach (Entity entity in entityRegistry.GetAllEntities())
        {
            double[]? position = null;
            if (entity is WorldEntity { Transform: { } transform })
            {
                NitroxVector3 world = transform.Position;
                position = [world.X, world.Y, world.Z];
            }
            builder.AppendLine(BridgeText.EntityDumpLine(entity.Id.ToString(), entity.TechType?.Name ?? "None", entity.GetType().Name, entity.ParentId?.ToString(), entity.ParentId == null ? "-" : "parent", position,
                                                         BridgeText.DescribeMetadata(entity.Metadata)));
        }
        return builder.ToString();
    }
}
#endif
