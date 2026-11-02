using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nitrox.Model.Constants;

namespace Nitrox.Server.Subnautica.Models.Serialization.World;

/// <summary>
///     One backup of the world per server start, made before the world is read (and so before a save upgrade or a save
///     can change it). The rolling backups of <see cref="Services.SaveService" /> are made after every save and only the
///     last <c>MaxBackups</c> are kept, so they reach back a few saves; these reach back five starts.
/// </summary>
/// <remarks>
///     The zips are in <c>backups/server-starts/</c> of the world and are named <c>ServerStart - yyyy-MM-dd HH.mm.ss.zip</c>.
///     Neither the pruning of the rolling backups nor the launcher's restore list sees them: both only look at the files
///     directly in <c>backups/</c> that start with "Backup - ". To restore one: stop the server and unpack the zip into the
///     world folder.
/// </remarks>
internal static class ServerStartBackups
{
    internal const string FOLDER_NAME = "server-starts";
    internal const string FILE_PREFIX = "ServerStart - ";
    internal const int KEPT = 5;

    internal static string FileNameFor(DateTime time)
    {
        return $"{FILE_PREFIX}{time.ToString(PersistanceConstants.BACKUP_DATE_TIME_FORMAT, CultureInfo.InvariantCulture)}.zip";
    }

    internal static bool TryParseTime(string fileName, out DateTime time)
    {
        time = default;
        if (!fileName.StartsWith(FILE_PREFIX, StringComparison.Ordinal) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string text = fileName.Substring(FILE_PREFIX.Length, fileName.Length - FILE_PREFIX.Length - ".zip".Length);
        return DateTime.TryParseExact(text, PersistanceConstants.BACKUP_DATE_TIME_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    /// <summary>
    ///     The start backups to delete so that <paramref name="keep" /> remain: the oldest. A file name that isn't a start
    ///     backup is never selected.
    /// </summary>
    /// <param name="fileNames">File names (not paths) in the folder of the start backups.</param>
    internal static List<string> SelectExpired(IEnumerable<string> fileNames, int keep)
    {
        List<(string Name, DateTime Time)> backups = [];
        foreach (string name in fileNames)
        {
            if (TryParseTime(name, out DateTime time))
            {
                backups.Add((name, time));
            }
        }

        return backups.OrderByDescending(backup => backup.Time)
                      .ThenByDescending(backup => backup.Name, StringComparer.Ordinal)
                      .Skip(Math.Max(1, keep))
                      .Select(backup => backup.Name)
                      .ToList();
    }

    /// <summary>
    ///     Zips the files of the world folder (not its subfolders, so no backups of backups) into the folder of the start
    ///     backups and deletes the oldest start backups beyond <paramref name="keep" />.
    /// </summary>
    /// <returns>The path of the new zip.</returns>
    internal static string CreateAndPrune(string saveDir, string backupsDir, DateTime now, int keep = KEPT)
    {
        string folder = Path.Combine(backupsDir, FOLDER_NAME);
        Directory.CreateDirectory(folder);

        string zipPath = Path.Combine(folder, FileNameFor(now));
        string tempPath = $"{zipPath}.tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (FileStream output = new(tempPath, FileMode.CreateNew, FileAccess.Write))
        using (ZipArchive zip = new(output, ZipArchiveMode.Create))
        {
            foreach (string file in Directory.GetFiles(saveDir))
            {
                // Fastest: the server waits for it, and the files are mostly JSON
                ZipArchiveEntry entry = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.Fastest);
                using Stream source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using Stream target = entry.Open();
                source.CopyTo(target);
            }
        }
        // Only a complete zip gets the name: a server killed while zipping leaves a .tmp, not a broken backup
        File.Move(tempPath, zipPath, true);

        List<string> existing = Directory.GetFiles(folder, $"{FILE_PREFIX}*.zip").Select(Path.GetFileName).OfType<string>().ToList();
        foreach (string expired in SelectExpired(existing, keep))
        {
            File.Delete(Path.Combine(folder, expired));
        }
        return zipPath;
    }
}
