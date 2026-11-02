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
    private readonly string[] extraArgs;
    private readonly List<FakePlayer> players = [];
    private IHost? host;
    private Task? pendingStart;

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

    private ServerHarness(string saveName, string[] extraArgs)
    {
        SaveName = saveName;
        this.extraArgs = extraArgs;
        DataPath = Path.Combine(Path.GetTempPath(), "nitrox-l2", $"{saveName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataPath);
        Port = GetFreeUdpPort();
    }

    /// <param name="saveName">Only used to name the save and the temp folder, the save is always new.</param>
    /// <param name="extraArgs">
    ///     Server command line arguments after the harness's own, so they override them (e.g.
    ///     <c>--GameServer:MaxBackups 2</c>). Used again at every restart.
    /// </param>
    public static async Task<ServerHarness> StartAsync(string saveName = "L2", string[]? extraArgs = null)
    {
        ServerHarness harness = new(saveName, extraArgs ?? []);
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
    ///     A new <see cref="FakePlayer" /> that only connected and got the session policy; the test does the rest of the
    ///     handshake. Disposed with the harness.
    /// </summary>
    public async Task<FakePlayer> ConnectAsync(string playerName)
    {
        FakePlayer player = await FakePlayer.ConnectAsync(playerName, Port);
        lock (players)
        {
            players.Add(player);
        }
        return player;
    }

    /// <summary>
    ///     Like <see cref="RestartAsync" />, but the new server doesn't load its world until <paramref name="worldLoadGate" />
    ///     completes, as if loading took that long. Returns while the server starts; the world loads and the start
    ///     finishes later (<see cref="WaitForStartAsync" />).
    /// </summary>
    public async Task RestartHoldingWorldLoadAsync(Task worldLoadGate)
    {
        await SaveAsync();
        await DisconnectPlayersAsync();
        await StopHostAsync();
        await StartHostAsync(worldLoadGate);
    }

    /// <summary>
    ///     Waits until the start begun by <see cref="RestartHoldingWorldLoadAsync" /> has finished.
    /// </summary>
    public async Task WaitForStartAsync()
    {
        if (pendingStart == null)
        {
            return;
        }
        await pendingStart.WaitAsync(FakePlayer.DefaultTimeout);
        pendingStart = null;
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
        if (pendingStart != null)
        {
            try
            {
                await pendingStart.WaitAsync(FakePlayer.DefaultTimeout);
            }
            catch (Exception ex)
            {
                // A start whose world load was held and never released; stop it anyway
                Console.WriteLine($"[L2] Server '{SaveName}' didn't finish starting: {ex.Message}");
            }
            pendingStart = null;
        }
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

    /// <param name="worldLoadGate">
    ///     Null: waits until the server has started. Otherwise the world load waits for it, and this returns once the
    ///     server accepts connections.
    /// </param>
    private async Task StartHostAsync(Task? worldLoadGate = null)
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
            $"--{SubnauticaServerOptions.CONFIG_SECTION_PATH}:{nameof(SubnauticaServerOptions.MaxBackups)}", "0",
            ..extraArgs
        ];
        ServerStartOptions startOptions = Program.ParseStartOptions(args);
        // Separate stopwatch: the server stops the one it gets once started
        HostApplicationBuilder builder = Program.CreateHostBuilder(args, startOptions, Stopwatch.StartNew(), interactive: false);
        // Debug output of a whole server is mostly batch spawning noise; the full log is in LogsPath anyway.
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        if (worldLoadGate != null)
        {
            HoldWorldLoad(builder.Services, worldLoadGate);
        }
        host = builder.Build();
        Task start = host.StartAsync();
        if (worldLoadGate != null)
        {
            // The network service starts within moments; a FakePlayer that connects earlier keeps trying for 5 s (LiteNetLib)
            pendingStart = start;
            Console.WriteLine($"[L2] Server '{SaveName}' starting on UDP {Port}, its world load is held");
            return;
        }
        await start;
        LastStartupTime = stopwatch.Elapsed;
        Console.WriteLine($"[L2] Server '{SaveName}' started in {LastStartupTime.TotalSeconds:0.00} s on UDP {Port}, data in {DataPath}");
    }

    /// <summary>
    ///     Makes the host start <see cref="WorldService" /> (which loads the world) only once <paramref name="gate" />
    ///     completes. The other services start as usual, so the network accepts players meanwhile, as it does while a
    ///     big world loads.
    /// </summary>
    private static void HoldWorldLoad(IServiceCollection services, Task gate)
    {
        // AddHostedSingletonService<WorldService> adds the singleton and right after it the hosted service resolving it
        int index = services.ToList().FindIndex(descriptor => descriptor.ServiceType == typeof(WorldService));
        if (index < 0 || index + 1 >= services.Count || services[index + 1].ServiceType != typeof(IHostedService))
        {
            throw new InvalidOperationException($"Can't find the hosted service registration of {nameof(WorldService)}");
        }
        services[index + 1] = ServiceDescriptor.Singleton<IHostedService>(provider => new GatedHostedService(provider.GetRequiredService<WorldService>(), gate));
    }

    private sealed class GatedHostedService(IHostedService inner, Task gate) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            await inner.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => inner.StopAsync(cancellationToken);
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
