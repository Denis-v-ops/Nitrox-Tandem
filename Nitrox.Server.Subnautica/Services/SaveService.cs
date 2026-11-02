using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Nitrox.Model.Constants;
using Nitrox.Model.Platforms.OS.Shared;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.AppEvents.Core;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Serialization.World;
using Nitrox.Server.Subnautica.Services.Core;

namespace Nitrox.Server.Subnautica.Services;

internal sealed class SaveService(Func<WorldService> worldServiceProvider, IPacketSender packetSender, ISaveState.Trigger saveStateTrigger, IOptions<SubnauticaServerOptions> options, IOptions<ServerStartOptions> startOptions, ILogger<SaveService> logger)
    : QueuingBackgroundService<SaveService.ServiceAction>, IHibernate
{
    private readonly ILogger<SaveService> logger = logger;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly IPacketSender packetSender = packetSender;
    private readonly ISaveState.Trigger saveStateTrigger = saveStateTrigger;
    private readonly IOptions<ServerStartOptions> startOptions = startOptions;
    private readonly Func<WorldService> worldServiceProvider = worldServiceProvider;
    private int saveNumber;

    public async Task QueueSaveAsync(CancellationToken cancellationToken, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1) => await QueueActionAsync(new ServiceAction(ServiceActionType.SAVE, callerFilePath, callerLineNumber), cancellationToken);

    protected override async Task ExecuteQueuedActionAsync(ServiceAction action, CancellationToken stoppingToken)
    {
        logger.ZLogTrace($"Save requested by {action.CallerFilePath}:{action.CallerLineNumber}");
        switch (action.Type)
        {
            case ServiceActionType.SAVE:
                string savePath = startOptions.Value.GetServerSavePath();
                // The players see the game's "Saving game" indicator from the start to the end (finished or failed) of the save
                int number = ++saveNumber;
                await AnnounceSaveAsync(number, ServerSaving.SavePhase.STARTED);
                bool saved = false;
                try
                {
                    if (!await worldServiceProvider().SaveAsync(savePath, stoppingToken))
                    {
                        return;
                    }
                    await saveStateTrigger.InvokeAsync(new ISaveState.Args(savePath));
                    saved = true;
                }
                finally
                {
                    await AnnounceSaveAsync(number, saved ? ServerSaving.SavePhase.FINISHED : ServerSaving.SavePhase.FAILED);
                }
                ExecutePostSaveCommand();
                BackUp(savePath);
                break;
        }
    }

    /// <summary>
    ///     Tells every connected player that a save starts or ends. A packet that can't be sent never keeps the world from
    ///     being saved.
    /// </summary>
    private async Task AnnounceSaveAsync(int number, ServerSaving.SavePhase phase)
    {
        try
        {
            await packetSender.SendPacketToAllAsync(new ServerSaving(number, phase));
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"Could not tell the players about save {number} ({phase})");
        }
    }

    private void ExecutePostSaveCommand()
    {
        string postSaveCommandPath = options.Value.PostSaveCommandPath;
        if (!string.IsNullOrWhiteSpace(postSaveCommandPath))
        {
            try
            {
                // Call external tool for backups, etc
                if (File.Exists(postSaveCommandPath))
                {
                    using Process process = Process.Start(new ProcessStartInfo
                    {
                        FileName = postSaveCommandPath,
                        Verb = "open"
                    });
                    logger.ZLogInformation($"Post-save command completed successfully: {postSaveCommandPath}");
                }
                else
                {
                    logger.ZLogError($"Post-save file does not exist: {postSaveCommandPath}");
                }
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"Post-save command failed");
            }
        }
    }

    private void BackUp(string saveDir)
    {
        if (options.Value.MaxBackups < 1)
        {
            logger.ZLogInformation($"No backup was made (\"{nameof(options.Value.MaxBackups)}\" is equal to 0)");
            return;
        }
        string backupDir = startOptions.Value.GetServerSaveBackupsPath();
        string tempOutDir = Path.Combine(backupDir, $"Backup - {DateTime.Now.ToString(PersistanceConstants.BACKUP_DATE_TIME_FORMAT)}");
        Directory.CreateDirectory(backupDir);

        try
        {
            // Prepare backup location
            Directory.CreateDirectory(tempOutDir);
            string newZipFile = $"{tempOutDir}.zip";
            if (File.Exists(newZipFile))
            {
                File.Delete(newZipFile);
            }
            foreach (string file in Directory.GetFiles(saveDir))
            {
                File.Copy(file, Path.Combine(tempOutDir, Path.GetFileName(file)));
            }

            FileSystem.Instance.ZipFilesInDirectory(tempOutDir, newZipFile);
            Directory.Delete(tempOutDir, true);
            logger.ZLogInformation($"World backed up");

            PruneBackups(backupDir, options.Value.MaxBackups);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"Error while backing up world");
            if (Directory.Exists(tempOutDir))
            {
                Directory.Delete(tempOutDir, true); // Delete the outZip folder that is sometimes left
            }
        }
    }

    /// <summary>
    ///     Deletes the oldest rolling backups beyond <paramref name="maxBackups" />. Only looks at the files directly in
    ///     <paramref name="backupDir" />, so the start backups in its subfolder stay (<see cref="ServerStartBackups" />).
    /// </summary>
    internal static void PruneBackups(string backupDir, int maxBackups)
    {
        FileInfo[] backups = Directory.EnumerateFiles(backupDir)
                                      .Select(f => new FileInfo(f))
                                      .Where(f => f is { Extension: ".zip" } info && info.Name.Contains("Backup - "))
                                      .OrderBy(f => File.GetCreationTime(f.FullName))
                                      .ToArray();
        if (backups.Length > maxBackups)
        {
            int numBackupsToDelete = backups.Length - Math.Max(1, maxBackups);
            for (int i = 0; i < numBackupsToDelete; i++)
            {
                backups[i].Delete();
            }
        }
    }

    async Task IEvent<IHibernate.SleepArgs>.OnEventAsync(IHibernate.SleepArgs args) => await QueueSaveAsync(CancellationToken.None);

    Task IEvent<IHibernate.WakeArgs>.OnEventAsync(IHibernate.WakeArgs args) => Task.CompletedTask;

    internal record ServiceAction(ServiceActionType Type, string CallerFilePath = "", int CallerLineNumber = -1);

    internal enum ServiceActionType
    {
        SAVE
    }
}
