using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Iskender.Infrastructure.Container.Telemetry;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Iskender.Container.Tests.Telemetry;

/// <summary>Container contract, Telemetry rules 1 and 2 (research R15), and FR-028: the subscription to traces and metrics, the service name, and the exporter only when its endpoint is set, configured by the other standard variables.</summary>
public class TelemetryTests
{
    [Fact]
    public void WithNoEndpointNoExporterIsRegistered()
    {
        var services = new ServiceCollection();

        ContainerTelemetry.AddIskenderContainerTelemetry(services, "iskender-data-plane", otlpEndpoint: null);

        Assert.DoesNotContain(services, IsOtlp);
    }

    [Fact]
    public void WithAnEndpointTheExporterIsRegistered()
    {
        var services = new ServiceCollection();

        ContainerTelemetry.AddIskenderContainerTelemetry(services, "iskender-data-plane", "http://127.0.0.1:4317");

        Assert.Contains(services, IsOtlp);
    }

    [Fact]
    public void TheOtherStandardVariablesReachTheExporterOptions()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ContainerTelemetry.EndpointVariable] = "http://127.0.0.1:4318",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "fixture-key=fixture-value",
        };
        using var provider = Provider("iskender-data-plane", environment);

        var options = provider.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>().Get(Options.DefaultName);

        Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol);
        Assert.Equal("fixture-key=fixture-value", options.Headers);
        Assert.Equal(new Uri("http://127.0.0.1:4318"), options.Endpoint);
    }

    [Fact]
    public void WithoutTheEndpointTheOtherVariablesAddNoExporter()
    {
        var services = new ServiceCollection();
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf" };
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(ContainerTelemetry.Variables(environment)).Build());

        ContainerTelemetry.AddIskenderContainerTelemetry(services, "iskender-data-plane", otlpEndpoint: null);

        Assert.DoesNotContain(services, IsOtlp);
    }

    [Fact]
    public void OnlyOtelVariablesAreGivenToTheSdk()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
            ["ISKENDER__CONTAINER__HEALTHPORT"] = "8081",
            ["ASPNETCORE_URLS"] = "http://*:5000",
            ["otel_lowercase"] = "x",
        };

        Assert.Equal(["OTEL_EXPORTER_OTLP_PROTOCOL"], ContainerTelemetry.Variables(environment).Select(variable => variable.Key));
    }

    [Fact]
    public void TheResourceNamesTheService()
    {
        using var provider = Provider("iskender-control-plane");

        var resource = provider.GetRequiredService<TracerProvider>().GetResource();

        Assert.Contains(resource.Attributes, attribute => attribute.Key == "service.name" && (string)attribute.Value == "iskender-control-plane");
    }

    [Theory]
    [InlineData(CoordinationTelemetry.SourceName)]
    [InlineData(ContainerTelemetry.PersistenceSource)]
    [InlineData(ContainerTelemetry.WorkSource)]
    public void AnActivityFromEachSourceIsRecorded(string sourceName)
    {
        using var provider = Provider("iskender-data-plane");
        _ = provider.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource(sourceName);

        using var activity = source.StartActivity("fixture.operation");

        Assert.NotNull(activity);
        Assert.True(activity.Recorded);
    }

    [Fact]
    public void AnotherSourceIsNotSubscribed()
    {
        using var provider = Provider("iskender-data-plane");
        _ = provider.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource("Iskender.Fixture.Unsubscribed");

        Assert.Null(source.StartActivity("fixture.operation"));
    }

    [Theory]
    [InlineData(CoordinationTelemetry.SourceName)]
    [InlineData(ContainerTelemetry.PersistenceSource)]
    [InlineData(ContainerTelemetry.WorkSource)]
    public void AMeasurementFromEachMeterIsCollected(string meterName) =>
        Assert.Contains(meterName, Collect(meterName));

    [Fact]
    public void AMeasurementFromAnotherMeterIsNotCollected() =>
        Assert.Empty(Collect("Iskender.Fixture.Unsubscribed"));

    // ISK-0009 T054: each plane's container, the worker's included, subscribes to the work source and meter.
    [Theory]
    [InlineData("iskender-data-plane")]
    [InlineData("iskender-control-plane")]
    [InlineData("iskender-worker")]
    public void EachPlaneRecordsAWorkActivity(string serviceName)
    {
        using var provider = Provider(serviceName);
        _ = provider.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource(ContainerTelemetry.WorkSource);

        using var activity = source.StartActivity("work.dispatch");

        Assert.NotNull(activity);
        Assert.True(activity.Recorded);
    }

    [Fact]
    public void TheWorkLiteralIsTheAdaptersWorkSource() =>
        Assert.Equal(
            typeof(PostgreSqlServiceCollectionExtensions).Assembly.GetType("Iskender.Infrastructure.PostgreSql.Work.WorkTelemetry", throwOnError: true)!.GetField("SourceName")!.GetRawConstantValue(),
            ContainerTelemetry.WorkSource);

    [Fact]
    public void ThePersistenceLiteralIsTheAdaptersDiagnosticsName() =>
        Assert.Equal(
            typeof(PostgreSqlServiceCollectionExtensions).Assembly.GetType("Iskender.Infrastructure.PostgreSql.PostgreSqlAdapter", throwOnError: true)!.GetField("DiagnosticsName")!.GetRawConstantValue(),
            ContainerTelemetry.PersistenceSource);

    // Records one measurement on a meter named meterName, flushes the container's MeterProvider through a reader added
    // beside its own, and returns the meter names the reader exported for that instrument. The instrument's name is unique,
    // since the provider subscribes by meter name across the process, where other tests record on the same meters.
    private static IReadOnlyCollection<string> Collect(string meterName)
    {
        var instrument = $"fixture.count.{Guid.NewGuid():N}";
        var exporter = new CollectingExporter();
        using var provider = Provider("iskender-data-plane", also: services =>
            services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddReader(new BaseExportingMetricReader(exporter))));
        var meters = provider.GetRequiredService<MeterProvider>();
        using var meter = new Meter(meterName);

        meter.CreateCounter<long>(instrument).Add(1);

        Assert.True(meters.ForceFlush());
        return [.. exporter.Metrics.Where(metric => metric.Name == instrument).Select(metric => metric.Meter)];
    }

    // The host's configuration, as the entry point gives it the OTEL_* variables.
    private static ServiceProvider Provider(string serviceName, IReadOnlyDictionary<string, string?>? environment = null, Action<IServiceCollection>? also = null)
    {
        var services = new ServiceCollection();
        environment ??= new Dictionary<string, string?>(StringComparer.Ordinal);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(ContainerTelemetry.Variables(environment)).Build());
        ContainerTelemetry.AddIskenderContainerTelemetry(services, serviceName, environment.GetValueOrDefault(ContainerTelemetry.EndpointVariable));
        also?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static bool IsOtlp(ServiceDescriptor descriptor) =>
        Names(descriptor.ServiceType).Concat(descriptor.ImplementationType is { } type ? Names(type) : []).Any(name => name.Contains("Otlp", StringComparison.Ordinal));

    private static IEnumerable<string> Names(Type type) => [type.FullName ?? type.Name, .. type.GenericTypeArguments.SelectMany(Names)];

    private sealed class CollectingExporter : BaseExporter<Metric>
    {
        private readonly ConcurrentQueue<(string Meter, string Name)> _metrics = new();

        public IReadOnlyCollection<(string Meter, string Name)> Metrics => [.. _metrics];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                _metrics.Enqueue((metric.MeterName, metric.Name));
            }

            return ExportResult.Success;
        }
    }
}
