using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Configuration;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica;
using Nitrox.Server.Subnautica.Extensions;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Serialization.World;

namespace Nitrox.Test.Helper.Headless;

/// <summary>
///     Runs the real Nitrox server (<c>Nitrox.Server.Subnautica</c>, all services) inside the test process, on a free UDP
///     port with its own temporary data directory. Players are <see cref="FakePlayer" />s. Used by L2 tests
///     (docs/fork/TESTING.md): what the server stores, reloads and sends to whom.
/// </summary>
/// <remarks>
///     Needs Subnautica installed (the server reads its asset files), not running. Left out compared to a normal
///     server: console input, the connection to a running Nitrox Launcher and the dev bridge.
/// </remarks>
internal sealed class ServerHarness : IAsyncDisposable
{
    private readonly List<FakePlayer> players = [];
    private IHost? host;

    /// <summary>
    ///     Temporary data directory (saves, logs). Deleted on dispose unless the environment variable
    ///     <c>NITROX_L2_KEEP</c> is set.
    /// </summary>
    public string DataPath { get; }

    public string SaveName { get; }
    public string SavePath => Path.Combine(DataPath, "saves", SaveName);
    public string LogsPath => Path.Combine(DataPath, "logs");
    public int Port { get; }

    /// <summary>
    ///     How long the last start took, from building the host until it accepted connections.
    /// </summary>
    public TimeSpan LastStartupTime { get; private set; }

    /// <summary>
    ///     The running server's DI container, for assertions on server state.
    /// </summary>
    public IServiceProvider Services => host?.Services ?? throw new InvalidOperationException("Server is not running");

    public EntityRegistry EntityRegistry => Get<EntityRegistry>();
    public WorldEntityManager WorldEntityManager => Get<WorldEntityManager>();

    private ServerHarness(string saveName)
    {
        SaveName = saveName;
        DataPath = Path.Combine(Path.GetTempPath(), "nitrox-l2", $"{saveName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataPath);
        Port = GetFreeUdpPort();
    }

    /// <param name="saveName">Only used to name the save and the temp folder, the save is always new.</param>
    public static async Task<ServerHarness> StartAsync(string saveName = "L2")
    {
        ServerHarness harness = new(saveName);
        try
        {
            await harness.StartHostAsync();
            return harness;
        }
        catch
        {
            await harness.DisposeAsync();
            throw;
        }
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>
    ///     A new <see cref="FakePlayer" /> that completed the join handshake. Disposed with the harness.
    /// </summary>
    public async Task<FakePlayer> JoinAsync(string playerName)
    {
        FakePlayer player = await FakePlayer.JoinAsync(playerName, Port);
        lock (players)
        {
            players.Add(player);
        }
        return player;
    }

    /// <summary>
    ///     Saves the world into <see cref="SavePath" />, like the <c>save</c> command, and waits until it's written.
    /// </summary>
    public async Task SaveAsync()
    {
        // WorldService.SaveAsync directly, because SaveService only queues the save. A save queued by the server itself
        // (e.g. when the last player leaves) can hold the files at the same moment; retry then.
        WorldService worldService = Get<WorldService>();
        IOptions<ServerStartOptions> startOptions = Get<IOptions<ServerStartOptions>>();
        for (int attempt = 1; ; attempt++)
        {
            if (await worldService.SaveAsync(startOptions.Value.GetServerSavePath(), CancellationToken.None))
            {
                return;
            }
            if (attempt >= 5)
            {
                throw new IOException($"Saving to {SavePath} failed, see the server log in {LogsPath}");
            }
            await Task.Delay(200);
        }
    }

    /// <summary>
    ///     Saves, disconnects all players, stops the server and starts a new one on the same save (same port). Players
    ///     have to join again.
    /// </summary>
    public async Task RestartAsync()
    {
        // Saving first: the server queues its own save once the last player has left, and two saves at once collide
        await SaveAsync();
        await DisconnectPlayersAsync();
        await StopHostAsync();
        await StartHostAsync();
    }

    /// <summary>
    ///     Disconnects every <see cref="FakePlayer" /> and waits until the server has dropped them, so the same names can
    ///     join again.
    /// </summary>
    public async Task DisconnectPlayersAsync()
    {
        DisconnectPlayers();
        PlayerManager playerManager = Get<PlayerManager>();
        await WaitUntilAsync(() => playerManager.GetConnectedPlayers().Count == 0, "the server has dropped all players");
    }

    /// <summary>
    ///     Polls server state, for packets that get no reply (most of them). Fails after <paramref name="timeout" />.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan wait = timeout ?? FakePlayer.DefaultTimeout;
        while (!condition())
        {
            if (stopwatch.Elapsed > wait)
            {
                Assert.Fail($"Timed out after {wait.TotalSeconds} s waiting until {because}");
            }
            await Task.Delay(20);
        }
    }

    public bool IsInCell(NitroxId id, AbsoluteEntityCell cell)
    {
        return WorldEntityManager.GetEntities(cell).Any(entity => entity.Id == id);
    }

    public async ValueTask DisposeAsync()
    {
        DisconnectPlayers();
        await StopHostAsync();
        if (Environment.GetEnvironmentVariable("NITROX_L2_KEEP") == null)
        {
            try
            {
                Directory.Delete(DataPath, true);
            }
            catch (IOException)
            {
                // A log file can still be held for a moment; it's a temp folder.
            }
        }
    }

    private async Task StartHostAsync()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string[] args =
        [
            "--save", SaveName,
            "--data-path", DataPath,
            "--embedded", "true",
            $"--{SubnauticaServerOptions.CONFIG_SECTION_PATH}:{nameof(SubnauticaServerOptions.ServerPort)}", Port.ToString(),
            $"--{SubnauticaServerOptions.CONFIG_SECTION_PATH}:{nameof(SubnauticaServerOptions.PortForward)}", "false",
            $"--{SubnauticaServerOptions.CONFIG_SECTION_PATH}:{nameof(SubnauticaServerOptions.LanDiscovery)}", "false",
            $"--{SubnauticaServerOptions.CONFIG_SECTION_PATH}:{nameof(SubnauticaServerOptions.AutoSave)}", "false",
            $"--{SubnauticaServerOptions.CONFIG_SECTION_PATH}:{nameof(SubnauticaServerOptions.MaxBackups)}", "0"
        ];
        ServerStartOptions startOptions = Program.ParseStartOptions(args);
        // Separate stopwatch: the server stops the one it gets once started
        HostApplicationBuilder builder = Program.CreateHostBuilder(args, startOptions, Stopwatch.StartNew(), interactive: false);
        // Debug output of a whole server is mostly batch spawning noise; the full log is in LogsPath anyway.
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        host = builder.Build();
        await host.StartAsync();
        LastStartupTime = stopwatch.Elapsed;
        Console.WriteLine($"[L2] Server '{SaveName}' started in {LastStartupTime.TotalSeconds:0.00} s on UDP {Port}, data in {DataPath}");
    }

    private async Task StopHostAsync()
    {
        if (host == null)
        {
            return;
        }
        IHost stopping = host;
        host = null;
        try
        {
            await stopping.StopAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            stopping.Dispose();
        }
    }

    private void DisconnectPlayers()
    {
        lock (players)
        {
            foreach (FakePlayer player in players)
            {
                player.Dispose();
            }
            players.Clear();
        }
    }

    private static int GetFreeUdpPort()
    {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}
