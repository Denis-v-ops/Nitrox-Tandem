using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Platforms.OS.Shared;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.Serialization.World;

namespace Nitrox.Server.Subnautica.Models.Commands;

/// <summary>
///     Server half of a bug report (the client's <c>/report</c> makes the other half on the player's PC). Saves a
///     snapshot of the world as it is right now, the recent server logs and server.cfg (without passwords) into
///     <c>reports/&lt;time&gt;_server_&lt;player&gt;.zip</c> on the server's machine. The live save isn't touched.
/// </summary>
[RequiresPermission(Perms.PLAYER)]
internal sealed class ReportCommand(Func<WorldService> worldServiceProvider, IOptions<ServerStartOptions> startOptions, ILogger<ReportCommand> logger) : ICommandHandler<string>
{
    private static readonly TimeSpan includeLogsWrittenWithin = TimeSpan.FromHours(24);
    private readonly ILogger<ReportCommand> logger = logger;
    private readonly IOptions<ServerStartOptions> startOptions = startOptions;
    private readonly Func<WorldService> worldServiceProvider = worldServiceProvider;

    [Description("Saves a bug report with a world snapshot and the server logs")]
    public async Task Execute(ICommandContext context, [Description("What went wrong")] string text = "")
    {
        logger.ZLogWarning($"Bug report by {context.OriginName}: {text}");
        string reportName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_server_{ToFileName(context.OriginName)}";
        string reportDir = Path.Combine(NitroxDirectory.ReportsPath, reportName);
        try
        {
            if (!await worldServiceProvider().SaveAsync(Path.Combine(reportDir, "save"), CancellationToken.None))
            {
                throw new IOException("World snapshot failed, see the log above");
            }
            CopyServerConfig(Path.Combine(reportDir, "save"));
            CopyRecentLogs(Path.Combine(reportDir, "logs"));
            await File.WriteAllTextAsync(Path.Combine(reportDir, "report.txt"),
                                         $"""
                                          Text: {text}
                                          Reported by: {context.OriginName}
                                          Local time: {DateTime.Now:O}
                                          UTC time: {DateTime.UtcNow:O}
                                          Nitrox: {NitroxEnvironment.Version} {NitroxEnvironment.GitHash}
                                          Save: {startOptions.Value.SaveName}
                                          """);

            string zipPath = FileSystem.Instance.ZipFilesInDirectory(reportDir, $"{reportDir}.zip");
            logger.ZLogInformation($"Bug report saved to {zipPath:@Path}");
            await context.ReplyAsync($"Server bug report saved to {zipPath} on the server's PC");
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"Failed to save bug report");
            await context.ReplyAsync("Failed to save the server bug report, see the server log");
        }
        finally
        {
            if (Directory.Exists(reportDir))
            {
                Directory.Delete(reportDir, true);
            }
        }
    }

    private void CopyServerConfig(string targetDir)
    {
        string configPath = startOptions.Value.GetServerConfigFilePath();
        if (!File.Exists(configPath))
        {
            return;
        }
        IEnumerable<string> lines = File.ReadLines(configPath).Select(line =>
        {
            string key = line.Split('=')[0];
            bool isPasswordSetting = !line.TrimStart().StartsWith('#') && line.Contains('=') && key.Contains("Password", StringComparison.OrdinalIgnoreCase);
            return isPasswordSetting ? $"{key}=<removed>" : line;
        });
        File.WriteAllLines(Path.Combine(targetDir, Path.GetFileName(configPath)), lines);
    }

    private void CopyRecentLogs(string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        DateTime minWriteTime = DateTime.Now - includeLogsWrittenWithin;
        foreach (string logFile in Directory.EnumerateFiles(startOptions.Value.GetServerLogsPath(), $"*_{startOptions.Value.SaveName}_*").Where(file => File.GetLastWriteTime(file) >= minWriteTime))
        {
            // The current log file is still open for writing.
            using FileStream source = new(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using FileStream target = File.Create(Path.Combine(targetDir, Path.GetFileName(logFile)));
            source.CopyTo(target);
        }
    }

    private static string ToFileName(string text) => new(text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}
