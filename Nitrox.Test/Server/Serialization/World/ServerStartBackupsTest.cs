using System.IO.Compression;
using System.Threading.Tasks;
using Nitrox.Model.Configuration;
using Nitrox.Server.Subnautica.Services;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Server.Subnautica.Models.Serialization.World;

/// <summary>
///     MULTINAUGHTICA_SPEC M3: a backup of the world at every server start, the last five kept, apart from the rolling
///     backups. Real file I/O in a temp folder.
/// </summary>
[TestClass]
public class ServerStartBackupsTest
{
    private string folder = null!;

    [TestInitialize]
    public void CreateFolder()
    {
        folder = Path.Combine(Path.GetTempPath(), $"nitrox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
    }

    [TestCleanup]
    public void DeleteFolder()
    {
        Directory.Delete(folder, true);
    }

    private static string Name(int day) => ServerStartBackups.FileNameFor(new DateTime(2026, 10, day, 22, 13, 2));

    [TestMethod]
    public void FileName_CarriesTheStartTime_AndIsNoRollingBackupName()
    {
        string name = ServerStartBackups.FileNameFor(new DateTime(2026, 10, 24, 22, 18, 36));

        name.Should().Be("ServerStart - 2026-10-24 22.18.36.zip");
        name.Should().NotContain("Backup - ", "the rolling pruning deletes zips with that in their name and the launcher lists the ones starting with it");
        ServerStartBackups.TryParseTime(name, out DateTime time).Should().BeTrue();
        time.Should().Be(new DateTime(2026, 10, 24, 22, 18, 36));
    }

    [TestMethod]
    public void SelectExpired_TheOldestBeyondTheLastFive()
    {
        List<string> names = Enumerable.Range(1, 8).Select(Name).ToList();

        ServerStartBackups.SelectExpired(names, 5).Should().BeEquivalentTo([Name(1), Name(2), Name(3)]);
        ServerStartBackups.SelectExpired(names.Take(5), 5).Should().BeEmpty();
        ServerStartBackups.SelectExpired([], 5).Should().BeEmpty();
    }

    [TestMethod]
    public void SelectExpired_NeverSelectsOtherFiles_AndKeepsAtLeastOne()
    {
        List<string> names = [Name(1), Name(2), "Backup - 2026-10-24 22.13.02.zip", "notes.txt", "ServerStart - not a date.zip", Name(3)];

        ServerStartBackups.SelectExpired(names, 2).Should().BeEquivalentTo([Name(1)]);
        ServerStartBackups.SelectExpired([Name(1), Name(2)], 0).Should().BeEquivalentTo([Name(1)]);
    }

    [TestMethod]
    public void SelectExpired_GoesByTimeNotByName()
    {
        string earlier = ServerStartBackups.FileNameFor(new DateTime(2026, 9, 30, 8, 0, 0));
        string later = ServerStartBackups.FileNameFor(new DateTime(2026, 10, 1, 8, 0, 0));

        ServerStartBackups.SelectExpired([later, earlier], 1).Should().Equal(earlier);
    }

    [TestMethod]
    public void CreateAndPrune_ZipsTheWorldFilesButNotItsSubfolders()
    {
        File.WriteAllText(Path.Combine(folder, "Version.json"), "version");
        File.WriteAllText(Path.Combine(folder, "EntityData.json"), "entities");
        string backups = Path.Combine(folder, "backups");
        Directory.CreateDirectory(backups);
        string rolling = Path.Combine(backups, "Backup - 2026-10-24 22.13.02.zip");
        File.WriteAllText(rolling, "rolling");

        string zipPath = ServerStartBackups.CreateAndPrune(folder, backups, new DateTime(2026, 10, 25, 9, 0, 0));

        zipPath.Should().Be(Path.Combine(backups, ServerStartBackups.FOLDER_NAME, "ServerStart - 2026-10-25 09.00.00.zip"));
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        zip.Entries.Select(entry => entry.FullName).Should().BeEquivalentTo(["Version.json", "EntityData.json"]);
        using StreamReader reader = new(zip.GetEntry("EntityData.json")!.Open());
        reader.ReadToEnd().Should().Be("entities");
        File.Exists(rolling).Should().BeTrue("the rolling backups are not touched");
        Directory.GetFiles(Path.Combine(backups, ServerStartBackups.FOLDER_NAME)).Should().ContainSingle("no .tmp is left");
    }

    [TestMethod]
    public void CreateAndPrune_KeepsTheLastFiveStarts()
    {
        File.WriteAllText(Path.Combine(folder, "Version.json"), "version");
        string backups = Path.Combine(folder, "backups");

        for (int day = 1; day <= 8; day++)
        {
            ServerStartBackups.CreateAndPrune(folder, backups, new DateTime(2026, 10, day, 12, 0, 0));
        }

        string[] left = Directory.GetFiles(Path.Combine(backups, ServerStartBackups.FOLDER_NAME)).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray();
        left.Should().Equal(Enumerable.Range(4, 5).Select(day => ServerStartBackups.FileNameFor(new DateTime(2026, 10, day, 12, 0, 0))));
    }

    /// <summary>
    ///     The pruning of the rolling backups (after every save) must not reach the start backups.
    /// </summary>
    [TestMethod]
    public void RollingBackupPruning_LeavesTheStartBackupsAlone()
    {
        File.WriteAllText(Path.Combine(folder, "Version.json"), "version");
        string backups = Path.Combine(folder, "backups");
        ServerStartBackups.CreateAndPrune(folder, backups, new DateTime(2026, 10, 1, 12, 0, 0));
        ServerStartBackups.CreateAndPrune(folder, backups, new DateTime(2026, 10, 2, 12, 0, 0));
        for (int day = 1; day <= 3; day++)
        {
            File.WriteAllText(Path.Combine(backups, $"Backup - 2026-10-0{day} 12.00.00.zip"), "rolling");
        }

        SaveService.PruneBackups(backups, 1);

        Directory.GetFiles(backups).Should().ContainSingle("the rolling backups are pruned to one");
        Directory.GetFiles(Path.Combine(backups, ServerStartBackups.FOLDER_NAME)).Should().HaveCount(2);
    }

    /// <summary>
    ///     L2: the real server makes the backup when it starts on an existing save, from the files as they were saved.
    /// </summary>
    [TestMethod]
    [TestCategory("L2")]
    public async Task ServerStart_OnAnExistingSave_BacksItUpFirst()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-StartBackup", [$"--{SubnauticaServerOptions.CONFIG_SECTION_PATH}:{nameof(SubnauticaServerOptions.MaxBackups)}", "2"]);
        string startBackups = Path.Combine(server.SavePath, "backups", ServerStartBackups.FOLDER_NAME);
        Directory.Exists(startBackups).Should().BeFalse("a new world has nothing to back up");
        await server.JoinAsync("Alice");

        await server.RestartAsync();

        string zipPath = Directory.GetFiles(startBackups).Should().ContainSingle().Subject;
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        zip.Entries.Select(entry => entry.FullName).Should().Contain(["Version.json", "PlayerData.json", "WorldData.json", "GlobalRootData.json", "EntityData.json"]);
        using StreamReader reader = new(zip.GetEntry("PlayerData.json")!.Open());
        reader.ReadToEnd().Should().Contain("Alice", "the save written before the restart is in it");
    }
}
