using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nitrox.Model.Core;
using Nitrox.Model.Networking;
using Nitrox.Model.Platforms.Discovery;
using Nitrox.Server.Subnautica.Models;
using Nitrox.Server.Subnautica.Models.Factories;
using Nitrox.Server.Subnautica.Models.Serialization;
using Nitrox.Server.Subnautica.Services;
using Nitrox.Server.Subnautica.Services.Core;

namespace Nitrox.Server.Subnautica;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.Handler;
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += AssemblyResolver.Handler;

        await StartupHostAsync(args);
    }

    /// <summary>
    ///     Initialize here so that the JIT can compile the EntryPoint method without having to resolve dependencies
    ///     that require the custom <see cref="AssemblyResolver.Handler" />.
    /// </summary>
    /// <remarks>
    ///     See <a href="https://stackoverflow.com/a/6089153/1277156">https://stackoverflow.com/a/6089153/1277156</a>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task StartupHostAsync(string[] args)
    {
        Stopwatch serverStartStopWatch = Stopwatch.StartNew();
        ConsoleUnhandledErrorHandler.Attach();

        ServerStartOptions startOptions = ParseStartOptions(args);
        await CreateHostBuilder(args, startOptions, serverStartStopWatch).Build().RunAsync();
    }

    /// <summary>
    ///     Parses console args into a config object for type-safety and fills in the paths that weren't given.
    /// </summary>
    internal static ServerStartOptions ParseStartOptions(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
                                           .AddCommandLine(args)
                                           .Build();
        ServerStartOptions startOptions = new();
        configuration.Bind(startOptions);

        if (startOptions.GamePath == null)
        {
            if (!GameInstallationFinder.FindGameCached(GameInfo.Subnautica))
            {
                throw new DirectoryNotFoundException("Could not find Subnautica installation.");
            }
            startOptions.GamePath = NitroxUser.GamePath;
        }
        startOptions.NitroxAppDataPath ??= NitroxDirectory.ConfigPath;
        startOptions.NitroxAssetsPath ??= NitroxUser.AssetsPath;

        // This hacky code is needed because server still requires game code to startup.
        // TODO: Do not depend on Assembly-Csharp types, only game files. Use proxy/stub types which can map to a Subnautica object.
        AssemblyResolver.GamePath = startOptions.GamePath;
        return startOptions;
    }

    /// <param name="args">Console args, they override the config files.</param>
    /// <param name="startOptions">Result of <see cref="ParseStartOptions" />.</param>
    /// <param name="serverStartStopWatch">Running since the start, logged once the server has started.</param>
    /// <param name="interactive">
    ///     False leaves out what a server hosted inside another process (tests) must not run: console input, the
    ///     connection to a running Nitrox Launcher, the dev bridge and the start-up status log (it looks up the WAN IP,
    ///     which takes seconds).
    /// </param>
    internal static HostApplicationBuilder CreateHostBuilder(string[] args, ServerStartOptions startOptions, Stopwatch serverStartStopWatch, bool interactive = true)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = NitroxEnvironment.DotnetEnvironment,
            ApplicationName = NitroxEnvironment.AppName
        });
        // Nitrox config can be overriden by development.json or command line args if supplied.
        builder.Configuration
               .AddNitroxConfigFile<SubnauticaServerOptions>(startOptions.GetServerConfigFilePath(), SubnauticaServerOptions.CONFIG_SECTION_PATH, true, true)
               .AddConditionalCsharpProjectJsonFile(builder.Environment.IsDevelopment(), "server.Development.json", typeof(Program).Namespace, true, true)
               .AddConditionalUpstreamJsonFile(builder.Environment.IsDevelopment(), "server.Development.json", true, true)
               .AddCommandLine(args)
            ;
        builder.Logging
               .SetMinimumLevel(builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information)
               .AddFilter(typeof(Program).Namespace, level => level > LogLevel.Trace || (level == LogLevel.Trace && Debugger.IsAttached))
               .AddFilter("Microsoft", LogLevel.Warning)
               .AddNitroxLogging()
            ;
        builder.Services
               .Configure<HostOptions>(options =>
               {
                   options.ServicesStartConcurrently = true;
                   options.ServicesStopConcurrently = true;
               })
               .AddNitroxOptions(startOptions)
               // Add initialization services - diagnoses the server environment on startup.
               .AddHostedSingletonService<PreventMultiServerInitService>()
               .AddHostedSingletonService<NetworkPortAvailabilityService>()
               // Add communication services
               .AddCommands();
        if (interactive)
        {
            builder.Services.AddConsoleInput();
        }
        builder.Services.AddPackets();
        if (interactive)
        {
            builder.Services.AddLocalServerManagement();
        }
        builder.Services
               // Add internal services
               .AddSingleton(GameInfo.Subnautica)
               .AddSubnauticaResources()
               .AddWorld()
               .AddSaving()
               .AddAppEvents()
               .AddAdminFeatures()
               .AddKeyedSingleton("startup", serverStartStopWatch)
               .AddHostedSingletonService<TaskQueueService>()
               .AddHostedSingletonService<HibernateService>();
        if (interactive)
        {
            builder.Services.AddHostedSingletonService<StatusService>();
        }
        else
        {
            builder.Services.AddSingleton<StatusService>(); // Still needed by the ip and summary commands
        }
        builder.Services
               .AddHostedSingletonService<PortForwardService>()
               .AddHostedSingletonService<LanBroadcastService>()
               .AddHostedSingletonService<MemoryService>()
               .AddHostedSingletonService<RestartService>()
               .AddHostedSingletonService<BanService>()
               .AddHostedSingletonService<IProgressReporter, ServerLoadingProgressService>()
               .AddSingleton<RandomFactory>()
               .AddSingleton<NtpSyncer>()
               .AddSingleton<SubnauticaServerProtoBufSerializer>()
               .AddSingleton<ServerJsonSerializer>()
            ;
#if DEBUG
        if (interactive)
        {
            // Lets agents run server commands from the shell (tools/bridge.ps1). Never in Release builds.
            builder.Services.AddHostedSingletonService<DevBridgeService>();
        }
#endif

        return builder;
    }
}
