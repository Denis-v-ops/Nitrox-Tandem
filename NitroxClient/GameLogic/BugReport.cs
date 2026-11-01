using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Helper;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic;

/// <summary>
///     Bundles what's needed to reproduce a problem a player just saw: the Nitrox logs, Unity's Player.log and what the
///     player wrote, into <c>reports/&lt;time&gt;_&lt;player&gt;.zip</c> next to the Nitrox logs folder. It also runs the
///     server's <c>report</c> command, which saves its own bundle with a world snapshot on the server's machine.
/// </summary>
public static class BugReport
{
    public const string COMMAND_NAME = "report";

    /// <summary>
    ///     Log files written within this time are included. Covers this session and, on the host's PC, the server.
    /// </summary>
    private static readonly TimeSpan includeLogsWrittenWithin = TimeSpan.FromHours(24);

    private static bool isSaving;

    /// <summary>
    ///     Handles the chat command <c>/report [text]</c>.
    /// </summary>
    /// <returns>True if <paramref name="command" /> was a report command.</returns>
    public static bool TryHandleCommand(string command, IMultiplayerSession session)
    {
        string[] parts = command.Split([' '], 2);
        if (!parts[0].Equals(COMMAND_NAME, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        Create(parts.Length > 1 ? parts[1].Trim() : "", session);
        return true;
    }

    public static void Create(string text, IMultiplayerSession session)
    {
        if (isSaving)
        {
            Log.InGame("A bug report is still being saved");
            return;
        }
        isSaving = true;

        string playerName = session.AuthenticationContext.Username;
        Log.Info($"Bug report by {playerName}: {text}");
        // Gather game state here, Unity objects can't be used from the worker thread below.
        string details = GetDetails(text, playerName, session);
        session.Send(new ServerCommand(string.IsNullOrWhiteSpace(text) ? COMMAND_NAME : $"{COMMAND_NAME} {text}"));

        string zipPath = Path.Combine(NitroxDirectory.ReportsPath, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{ToFileName(playerName)}.zip");
        List<string> files = GetFilesToInclude().ToList();
        Task saveTask = Task.Run(() => WriteZip(zipPath, details, files));
        CoroutineHost.StartCoroutine(ReportWhenSaved(saveTask, zipPath));
    }

    private static IEnumerator ReportWhenSaved(Task saveTask, string zipPath)
    {
        yield return new WaitUntil(() => saveTask.IsCompleted);
        isSaving = false;
        if (saveTask.Exception != null)
        {
            Log.Error(saveTask.Exception, "Failed to save bug report");
            Log.InGame("Failed to save the bug report, see the Nitrox log");
            yield break;
        }
        Log.Info($"Bug report saved to {zipPath}");
        Log.InGame($"Bug report saved to {zipPath}");
    }

    private static string GetDetails(string text, string playerName, IMultiplayerSession session)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Text: {text}");
        builder.AppendLine($"Player: {playerName}");
        builder.AppendLine($"Local time: {DateTime.Now:O}");
        builder.AppendLine($"UTC time: {DateTime.UtcNow:O}");
        builder.AppendLine($"Nitrox: {NitroxEnvironment.Version} {NitroxEnvironment.GitHash}");
        builder.AppendLine($"Server: {session.IpAddress}:{session.ServerPort}");
        if (Player.main)
        {
            builder.AppendLine($"Position: {Player.main.transform.position}");
            SubRoot currentSub = Player.main.currentSub;
            if (currentSub)
            {
                builder.AppendLine($"Inside: {currentSub.name} {GetIdText(currentSub)}");
            }
            Vehicle vehicle = Player.main.GetVehicle();
            if (vehicle)
            {
                builder.AppendLine($"Vehicle: {vehicle.name} {GetIdText(vehicle)}");
            }
        }
        return builder.ToString();

        static string GetIdText(Component component) => component.TryGetNitroxId(out NitroxId id) ? $"(id {id})" : "(no id)";
    }

    private static IEnumerable<string> GetFilesToInclude()
    {
        DateTime minWriteTime = DateTime.Now - includeLogsWrittenWithin;
        if (Directory.Exists(Log.LogDirectory))
        {
            foreach (string logFile in Directory.GetFiles(Log.LogDirectory, "*.log").Where(file => File.GetLastWriteTime(file) >= minWriteTime))
            {
                yield return logFile;
            }
        }

        // Unity keeps the previous run's log as Player-prev.log, which matters after a crash.
        string playerLog = Application.consoleLogPath;
        if (!string.IsNullOrEmpty(playerLog))
        {
            string previousPlayerLog = Path.Combine(Path.GetDirectoryName(playerLog) ?? "", $"{Path.GetFileNameWithoutExtension(playerLog)}-prev{Path.GetExtension(playerLog)}");
            foreach (string file in new[] { playerLog, previousPlayerLog }.Where(file => File.Exists(file) && File.GetLastWriteTime(file) >= minWriteTime))
            {
                yield return file;
            }
        }
    }

    private static void WriteZip(string zipPath, string details, List<string> files)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using ZipArchive zip = new(File.Create(zipPath), ZipArchiveMode.Create);
        using (StreamWriter writer = new(zip.CreateEntry("report.txt").Open()))
        {
            writer.Write(details);
        }
        foreach (string file in files)
        {
            try
            {
                // Log files are still open for writing by Nitrox and Unity.
                using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using Stream target = zip.CreateEntry(Path.GetFileName(file)).Open();
                source.CopyTo(target);
            }
            catch (Exception ex)
            {
                Log.Warn($"Bug report: could not include {file}: {ex.Message}");
            }
        }
    }

    private static string ToFileName(string text) => new(text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}
