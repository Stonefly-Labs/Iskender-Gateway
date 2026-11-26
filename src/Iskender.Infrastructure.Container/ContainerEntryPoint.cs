using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Authentication;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container.Diagnostics;
using Iskender.Infrastructure.Container.Health;
using Iskender.Infrastructure.Container.Lifecycle;
using Iskender.Infrastructure.Container.Telemetry;
using Iskender.Infrastructure.Coordination;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>The control plane's command that issues an operator token, whose logs go to standard error (ISK-0010).</summary>
    public const string BootstrapCommand = "bootstrap";

    private const string ConfigurationCodePrefix = "config.";

    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Parses <paramref name="args"/> before reading any configuration, then serves, runs the <c>health</c> command, serves a
    /// serving variant <paramref name="definition"/> adds, or runs a command it adds, and returns the exit code. Anything
    /// else, a serving variant's name followed by any argument included, writes usage to standard error and returns 64.
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
        TextWriter? standardError = null,
        TextWriter? standardOutput = null)
    {
        if (args is null || definition is null || environment is null || time is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var run = new Run(definition, environment, files ?? FileSystemProbe.Instance, time, standardError ?? Console.Error, stopToken)
        {
            StandardOutput = standardOutput ?? Console.Out,
        };
        if (args.Length == 0)
        {
            using var loggers = Loggers(loggerProvider);
            return await ServeAsync(run, loggers).ConfigureAwait(false);
        }

        if (args[0] == HealthCommand)
        {
            return args is [_, "live" or "ready"] ? await HealthAsync(run, args[1]).ConfigureAwait(false) : WriteUsage(run);
        }

        if (definition.ServingCommands.TryGetValue(args[0], out var serving))
        {
            if (args.Length != 1)
            {
                return WriteUsage(run);
            }

            using var loggers = Loggers(loggerProvider);
            return await ServeAsync(run with { Definition = Variant(serving), Check = serving.Check }, loggers).ConfigureAwait(false);
        }

        if (definition.Commands.TryGetValue(args[0], out var command))
        {
            // bootstrap writes only its token to standard output, so its logs go to standard error (Bootstrap command
            // rule 4, Diagnostics rule 9).
            using var loggers = Loggers(loggerProvider, toStandardError: args[0] == BootstrapCommand);
            CompositionSources Resolve()
            {
                try
                {
                    return ConfigurationRoots.Resolve(environment, run.Files);
                }
                catch (GatewayException rejected) when (rejected.Code == ErrorCodes.HostingConfigurationRejected)
                {
                    CompositionLog.LogStartRefused(loggers.CreateLogger(CompositionLog.Category), 1);
                    throw;
                }
            }

            try
            {
                return await command(args[1..], new ContainerCommandContext(definition, loggers, time, run.StandardError, Resolve) { StandardOutput = run.StandardOutput }, stopToken).ConfigureAwait(false);
            }
            catch (Exception unexpected)
            {
                // Only the type name, never the message or the runtime's unhandled-exception trace (Diagnostics rules 4 and 6).
                await run.StandardError.WriteLineAsync(unexpected.GetType().Name).ConfigureAwait(false);
                return ExitCodes.Unexpected;
            }
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

                // Settings a module checks across fields when they are first resolved (ISK-0005's lease durations and Redis
                // credentials, and, through the persistence probe, ISK-0004's pool sizes) are resolved here, after validation
                // and before anything binds, so a configuration they reject refuses the start as validation does. A serving
                // variant's check resolves its own modules' settings, such as ISK-0009's work section, at the same point.
                app.Services.GetService<CoordinationSettings>();
                app.Services.GetService<IPersistenceHealthProbe>();
                run.Check?.Invoke(app.Services);
            }
            catch (GatewayException refused) when (refused.Code == ErrorCodes.OperatorTransportUnprotected || refused.Code == ErrorCodes.OperatorCertificateUnusable)
            {
                // 7002's {Reason} names the failing field, such as iskender.container.tlsKeyFile, and never the file's path or
                // content (Diagnostics rule 2; maintainer decision, 2026-11-26, implement; CONV-0010-003).
                ContainerLog.LogStartRefused(logger, refused.Code.Id, ExitCodes.ConfigurationRejected, reason: Diagnostic(refused, "field"));
                return ExitCodes.ConfigurationRejected;
            }
            catch (GatewayException refused) when (refused.Code == ErrorCodes.HostingConfigurationRejected || refused.Code.Id.StartsWith(ConfigurationCodePrefix, StringComparison.Ordinal))
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
            // In listener mode the traffic listener is in-process TLS 1.2 or later from the pair the settings loaded once; the
            // health listener, and the traffic listener in the other modes, are plain HTTP (Listeners rules 1 and 7, R13).
            if (run.Definition.MapTraffic is not null && settings.TlsCertificate is { } certificate)
            {
                kestrel.ListenAnyIP(settings.TrafficPort, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = certificate,
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                }));
            }
            else if (run.Definition.MapTraffic is not null)
            {
                kestrel.ListenAnyIP(settings.TrafficPort);
            }

            kestrel.ListenAnyIP(settings.HealthPort);

            var healthPort = settings.HealthPort;
            app.MapWhen(context => context.Connection.LocalPort == healthPort, health => health.Run(HealthEndpoints.HandleAsync));
            app.UseRouting();
            run.Definition.MapTraffic?.Invoke(app);

            // HostOptions is built with the application, and the host reads ShutdownTimeout only when it stops, so it is set
            // here as the listeners are: shutdownDelay for StoppingAsync, then shutdownTimeout for the server's drain.
            app.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout = settings.ShutdownDelay + settings.ShutdownTimeout;

            // Every stop signal reaches StopApplication: the console lifetime's, and in tests the stop token. The lifecycle
            // turns stopping and writes 7004 on the first, during start-up too, which StopApplication also cancels.
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stopping = app.Lifetime.ApplicationStopping.Register(() =>
            {
                app.Services.GetService<ContainerLifecycle>()?.BeginStopping();
                stopped.TrySetResult();
            });
            using var stop = run.StopToken.Register(app.Lifetime.StopApplication);
            try
            {
                await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cancelled) when (app.Lifetime.ApplicationStopping.IsCancellationRequested && IsCancellation(cancelled))
            {
                // Stopped during start-up: the start has finished or been cancelled, so nothing is disposed under it, and
                // the rest of the sequence runs (Lifecycle rule 2). Services that start together report their
                // cancellations together.
            }
            catch (GatewayException refused) when (refused.Code == ErrorCodes.CoordinationCapabilityUnsupported)
            {
                // The composition has written 6004 and stopped what it started (hosting contract, Start-up rule 7).
                ContainerLog.LogStartRefused(
                    logger, refused.Code.Id, ExitCodes.MandatoryFeatureRefused, Diagnostic(refused, "feature"), Diagnostic(refused, "module"), Diagnostic(refused, "reason"));
                return ExitCodes.MandatoryFeatureRefused;
            }
            catch (GatewayException refused) when (refused.Code == ErrorCodes.HostingRegistrationMissing)
            {
                // A coordinator the configuration selects with no registration, found once validation has passed.
                return Refused(logger, refused, ExitCodes.RegistrationMissing);
            }
            catch (GatewayException refused) when (refused.Code == ErrorCodes.OperatorRouteUndeclared)
            {
                // The admin route check runs in StartingAsync, before the server's start binds any listener (Start-up rule 6).
                return Refused(logger, refused, ExitCodes.RouteUndeclared);
            }

            await stopped.Task.ConfigureAwait(false);
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
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
    // Iskender or a listener (research R7); only the OTEL_* variables are added, for the SDK.
    private static WebApplication Build(Run run, ILoggerFactory loggers, CompositionSources sources)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.WebHost.UseKestrelCore();

        // UseKestrelCore leaves out HTTPS support, which UseHttps needs. It is added for any plane with a traffic listener,
        // since the transport mode is read only after the container is built; it binds nothing until UseHttps is called.
        if (run.Definition.MapTraffic is not null)
        {
            builder.WebHost.UseKestrelHttpsConfiguration();
        }
        builder.Services.AddRoutingCore();
        builder.Services.AddSingleton(loggers);
        builder.Services.AddSingleton(run.Time);
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

        // WebApplicationBuilder adds the web server's hosted service after every service the application registers, so
        // in order the listeners would bind only once every composition start-up had finished. Starting the hosted
        // services together binds them while the start-ups run, with the lifecycle check starting (data-model
        // "Lifecycle", TR-8). The stop order stays sequential, which research R5 relies on.
        builder.Services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true);
        run.Definition.Compose(builder.Services, sources);
        if (run.Definition.MapTraffic is not null)
        {
            builder.Services.AddSingleton(new ContainerTrafficListener());
        }

        // Only the OTEL_* variables reach the host's configuration, which only the OpenTelemetry SDK reads: no listener
        // setting has that prefix, and Iskender reads its own sources (research R7, Telemetry rule 2).
        builder.Configuration.AddInMemoryCollection(ContainerTelemetry.Variables(run.Environment));
        run.Environment.TryGetValue(ContainerTelemetry.EndpointVariable, out var otlpEndpoint);
        ContainerTelemetry.AddIskenderContainerTelemetry(builder.Services, run.Definition.ServiceName, otlpEndpoint);
        return builder.Build();
    }

    // The configuration is read through the core with no logger, so the check writes no event (research R13). The container
    // gets the validation every other container does, with a logger factory that writes nothing. The check cannot tell
    // which command the container serves, so it reads the document with the sections of the plane and of every serving
    // variant, and a worker's document, with its work section, checks as the plane's does (research R11).
    private static async Task<int> HealthAsync(Run run, string check)
    {
        int port;
        try
        {
            var sources = ConfigurationRoots.Resolve(run.Environment, run.Files);
            var schemas = new Dictionary<string, SectionSchema>(StringComparer.Ordinal);
            foreach (var compose in run.Definition.ServingCommands.Values.Select(serving => serving.Compose).Prepend(run.Definition.Compose))
            {
                foreach (var schema in (await SchemasAsync(run, compose, sources).ConfigureAwait(false)).Schemas)
                {
                    schemas.TryAdd(schema.Namespace, schema);
                }
            }

            if (CompositionConfiguration.Probe(sources, ConfigurationSchemaRegistry.Create(schemas.Values)) is not { } snapshot)
            {
                return ExitCodes.Unexpected;
            }

            port = ContainerSettings.From(snapshot.GetSection(ContainerSectionSchema.Schema), snapshot.EnvironmentName, hasTrafficListener: false).HealthPort;
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

    private static async Task<ConfigurationSchemaRegistry> SchemasAsync(
        Run run, Action<IServiceCollection, CompositionSources> compose, CompositionSources sources)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(run.Time);
        compose(services, sources);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return provider.GetRequiredService<ConfigurationSchemaRegistry>();
    }

    private static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException
        || (exception is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException));

    private static string? Diagnostic(GatewayException refused, string name) =>
        refused.Error?.Diagnostics.Values.FirstOrDefault(pair => pair.Key == name).Value;

    private static int Refused(ILogger logger, GatewayException refused, int exitCode)
    {
        ContainerLog.LogStartRefused(logger, refused.Code.Id, exitCode);
        return exitCode;
    }

    private static int WriteUsage(Run run)
    {
        var serving = string.Concat(run.Definition.ServingCommands.Keys.Order(StringComparer.Ordinal).Select(name => $"|{name}"));
        var commands = string.Concat(run.Definition.Commands.Keys.Order(StringComparer.Ordinal).Select(name => $"|{name} ..."));
        run.StandardError.WriteLine($"usage: [health live|health ready{serving}{commands}]");
        return ExitCodes.Usage;
    }

    // A serving variant runs as a definition of its own, with only the health listener, and no command or variant of its own.
    private static ContainerPlaneDefinition Variant(ContainerServingCommand serving) =>
        new(serving.Plane, serving.ServiceName, serving.Compose, null, new Dictionary<string, ContainerCommand>());

    // One JSON object per line on standard output, or standard error, with no scope, and Microsoft categories at Warning,
    // so ASP.NET Core's request logs, which hold paths and hosts, are never written (container contract, Telemetry rule 3).
    private static ILoggerFactory Loggers(ILoggerProvider? provider, bool toStandardError = false) =>
        provider is not null
            ? new LoggerFactory([provider])
            : LoggerFactory.Create(logging => logging
                .SetMinimumLevel(LogLevel.Information)
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("Iskender", LogLevel.Information)
                .AddJsonConsole(options => options.IncludeScopes = false)
                .AddConsole(options => options.LogToStandardErrorThreshold = toStandardError ? LogLevel.Trace : LogLevel.None));

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
        CancellationToken StopToken,
        Action<IServiceProvider>? Check = null)
    {
        /// <summary>Where a command writes its only output, such as <c>bootstrap</c>'s token (ISK-0010).</summary>
        public TextWriter StandardOutput { get; init; } = Console.Out;
    }
}
