using System.Collections;
using System.Net;
using System.Reflection;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container.Diagnostics;
using Iskender.Infrastructure.Container.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Iskender.Infrastructure.Container;

/// <summary>
/// The container entry point both executables return from <c>Main</c> (container contract, Command line and exit codes,
/// Start-up rule 1, and Listeners; research R7, R9, R10, and R13). Owned by ISK-0007.
/// </summary>
public static class ContainerEntryPoint
{
    /// <summary>The command that checks a running container's health listener.</summary>
    public const string HealthCommand = "health";

    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Parses <paramref name="args"/> before reading any configuration, then serves, runs the <c>health</c> command, or runs
    /// a command <paramref name="definition"/> adds, and returns the exit code. Anything else writes usage to standard
    /// error and returns 64.
    /// </summary>
    public static Task<int> RunAsync(string[] args, ContainerPlaneDefinition definition) =>
        RunAsync(args, definition, ProcessEnvironment(), TimeProvider.System, CancellationToken.None);

    /// <summary>
    /// As <see cref="RunAsync(string[], ContainerPlaneDefinition)"/>, with the environment, file system, clock, stop
    /// signal, and log provider given, for tests. Cancelling <paramref name="stopToken"/> is a stop signal.
    /// </summary>
    internal static async Task<int> RunAsync(
        string[] args,
        ContainerPlaneDefinition definition,
        IReadOnlyDictionary<string, string?> environment,
        TimeProvider time,
        CancellationToken stopToken,
        ILoggerProvider? loggerProvider = null,
        IFileSystemProbe? files = null,
        TextWriter? standardError = null)
    {
        if (args is null || definition is null || environment is null || time is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var run = new Run(definition, environment, files ?? FileSystemProbe.Instance, time, standardError ?? Console.Error, stopToken);
        if (args.Length == 0)
        {
            using var loggers = Loggers(loggerProvider);
            return await ServeAsync(run, loggers).ConfigureAwait(false);
        }

        if (args[0] == HealthCommand)
        {
            return args is [_, "live" or "ready"] ? await HealthAsync(run, args[1]).ConfigureAwait(false) : WriteUsage(run);
        }

        if (definition.Commands.TryGetValue(args[0], out var command))
        {
            using var loggers = Loggers(loggerProvider);
            CompositionSources sources;
            try
            {
                sources = ConfigurationRoots.Resolve(environment, run.Files);
            }
            catch (GatewayException rejected) when (rejected.Code == ErrorCodes.HostingConfigurationRejected)
            {
                CompositionLog.LogStartRefused(loggers.CreateLogger(CompositionLog.Category), 1);
                return ExitCodes.ConfigurationRejected;
            }

            return await command(args[1..], sources, new ContainerCommandContext(definition, loggers, time, run.StandardError), stopToken).ConfigureAwait(false);
        }

        return WriteUsage(run);
    }

    private static async Task<int> ServeAsync(Run run, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(ContainerLog.Category);
        var plane = run.Definition.PlaneName;
        ContainerLog.LogStarting(logger, plane, Release());

        int exitCode;
        string? failure = null;
        try
        {
            exitCode = await ServeCoreAsync(run, loggers, logger).ConfigureAwait(false);
        }
        catch (Exception unexpected)
        {
            exitCode = ExitCodes.Unexpected;
            failure = unexpected.GetType().Name;
        }

        ContainerLog.LogStopped(logger, plane, exitCode, failure);
        return exitCode;
    }

    private static async Task<int> ServeCoreAsync(Run run, ILoggerFactory loggers, ILogger logger)
    {
        CompositionSources sources;
        try
        {
            sources = ConfigurationRoots.Resolve(run.Environment, run.Files);
        }
        catch (GatewayException rejected) when (rejected.Code == ErrorCodes.HostingConfigurationRejected)
        {
            CompositionLog.LogStartRefused(loggers.CreateLogger(CompositionLog.Category), 1);
            return Refused(logger, rejected, ExitCodes.ConfigurationRejected);
        }

        var app = Build(run, loggers, sources);
        await using (app.ConfigureAwait(false))
        {
            ContainerSettings settings;
            try
            {
                await Lifecycle(app.Services, loggers).EnsureComposableAsync(run.StopToken).ConfigureAwait(false);
                settings = app.Services.GetRequiredService<ContainerSettings>();
            }
            catch (GatewayException refused) when (refused.Code == ErrorCodes.HostingConfigurationRejected || refused.Code == ErrorCodes.ConfigFieldOutOfRange)
            {
                return Refused(logger, refused, ExitCodes.ConfigurationRejected);
            }
            catch (GatewayException refused) when (refused.Code == ErrorCodes.HostingRegistrationMissing)
            {
                return Refused(logger, refused, ExitCodes.RegistrationMissing);
            }

            // WebApplication builds its server, and the server's options, in Build(); Kestrel reads the listen options only
            // when it starts, so both listeners are added here, after validation and before anything binds.
            var kestrel = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
            if (run.Definition.MapTraffic is not null)
            {
                kestrel.ListenAnyIP(settings.TrafficPort);
            }

            kestrel.ListenAnyIP(settings.HealthPort);

            var healthPort = settings.HealthPort;
            app.MapWhen(context => context.Connection.LocalPort == healthPort, health => health.Run(HealthEndpoints.HandleAsync));
            app.UseRouting();
            run.Definition.MapTraffic?.Invoke(app);

            using var stop = run.StopToken.Register(app.Lifetime.StopApplication);
            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await app.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            return ExitCodes.Success;
        }
    }

    // With no composition root there is no lifecycle: the start fails as the hosting contract's check does, naming hosting.
    private static CompositionLifecycle Lifecycle(IServiceProvider services, ILoggerFactory loggers) =>
        services.GetService<CompositionLifecycle>()
        ?? throw CompositionLog.RegistrationMissing(
            loggers.CreateLogger(CompositionLog.Category),
            CompositionRoot.Requirement,
            services.GetServices<CompositionModule>().Select(module => module.Name).Order(StringComparer.Ordinal).FirstOrDefault() ?? ContainerServiceCollectionExtensions.Module);

    // CreateEmptyBuilder adds no configuration source, so no ASP.NET Core setting, file, variable, or argument reaches
    // Iskender or a listener (research R7).
    private static WebApplication Build(Run run, ILoggerFactory loggers, CompositionSources sources)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.WebHost.UseKestrelCore();
        builder.Services.AddRoutingCore();
        builder.Services.AddSingleton(loggers);
        builder.Services.AddSingleton(run.Time);
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
        run.Definition.Compose(builder.Services, sources);
        return builder.Build();
    }

    // The configuration is read through the core with no logger, so the check writes no event (research R13).
    private static async Task<int> HealthAsync(Run run, string check)
    {
        int port;
        try
        {
            var sources = ConfigurationRoots.Resolve(run.Environment, run.Files);
            var services = new ServiceCollection();
            run.Definition.Compose(services, sources);
            ConfigurationSchemaRegistry registry;
            await using (var provider = services.BuildServiceProvider())
            {
                registry = provider.GetRequiredService<ConfigurationSchemaRegistry>();
            }

            if (CompositionConfiguration.Probe(sources, registry) is not { } snapshot)
            {
                return ExitCodes.Unexpected;
            }

            port = ContainerSettings.From(snapshot.GetSection(ContainerSectionSchema.Schema)).HealthPort;
        }
        catch (GatewayException)
        {
            return ExitCodes.Unexpected;
        }

        using var client = new HttpClient { Timeout = HealthTimeout };
        try
        {
            using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/health/{check}"), run.StopToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK ? ExitCodes.Success : ExitCodes.Unexpected;
        }
        catch (Exception failed) when (failed is HttpRequestException or OperationCanceledException)
        {
            return ExitCodes.Unexpected;
        }
    }

    private static int Refused(ILogger logger, GatewayException refused, int exitCode)
    {
        ContainerLog.LogStartRefused(logger, refused.Code.Id, exitCode);
        return exitCode;
    }

    private static int WriteUsage(Run run)
    {
        var commands = string.Concat(run.Definition.Commands.Keys.Order(StringComparer.Ordinal).Select(name => $"|{name} ..."));
        run.StandardError.WriteLine($"usage: [health live|health ready{commands}]");
        return ExitCodes.Usage;
    }

    private static ILoggerFactory Loggers(ILoggerProvider? provider) =>
        provider is not null
            ? new LoggerFactory([provider])
            : LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information).AddJsonConsole());

    private static string? Release() =>
        (Assembly.GetEntryAssembly() ?? typeof(ContainerEntryPoint).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static Dictionary<string, string?> ProcessEnvironment()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            values[(string)entry.Key] = entry.Value as string;
        }

        return values;
    }

    private sealed record Run(
        ContainerPlaneDefinition Definition,
        IReadOnlyDictionary<string, string?> Environment,
        IFileSystemProbe Files,
        TimeProvider Time,
        TextWriter StandardError,
        CancellationToken StopToken);
}
