using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Iskender.Infrastructure.Container.Telemetry;

/// <summary>
/// The container's OpenTelemetry subscription (container contract, Telemetry rules 1 and 2; research R15). No ASP.NET Core
/// instrumentation is added, so no request route is recorded. Owned by ISK-0007.
/// </summary>
internal static class ContainerTelemetry
{
    /// <summary>
    /// The persistence adapter's activity source and meter, as the persistence contract's Telemetry section fixes it. A
    /// literal, because this library may not reference <c>Iskender.Infrastructure.PostgreSql</c>; a test asserts it equals
    /// <c>PostgreSqlAdapter.DiagnosticsName</c>.
    /// </summary>
    public const string PersistenceSource = "Iskender.Persistence";

    /// <summary>
    /// The work activity source and meter, as the work contract's Tracing and Diagnostics sections fix it (ISK-0009). A
    /// literal for the same reason as <see cref="PersistenceSource"/>; a test asserts it equals the adapter's
    /// <c>WorkTelemetry.SourceName</c>.
    /// </summary>
    public const string WorkSource = "Iskender.Work";

    /// <summary>The variable that, set, adds the OTLP exporter.</summary>
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The prefix of the standard variables the SDK reads, which are the only ones given to it.</summary>
    public const string VariablePrefix = "OTEL_";

    /// <summary>
    /// The <c>OTEL_*</c> entries of <paramref name="environment"/>, which the entry point adds to the host's configuration,
    /// where only the SDK reads them, so they configure the exporter (Telemetry rule 2). No other variable is given.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>> Variables(IReadOnlyDictionary<string, string?> environment) =>
        environment.Where(variable => variable.Key.StartsWith(VariablePrefix, StringComparison.Ordinal));

    /// <summary>
    /// Subscribes to <c>Iskender.Coordination</c>, <c>Iskender.Persistence</c>, and <c>Iskender.Work</c> with resource <c>service.name</c>
    /// <paramref name="serviceName"/>, and adds the OTLP exporter for traces and metrics only when
    /// <paramref name="otlpEndpoint"/> is set, so nothing is exported otherwise. The exporter reads the endpoint and the
    /// other standard variables from the configuration the host registers, so the SDK adds each signal's path for
    /// <c>http/protobuf</c>; an endpoint that is not an absolute URI fails here.
    /// </summary>
    public static void AddIskenderContainerTelemetry(IServiceCollection services, string serviceName, string? otlpEndpoint)
    {
        var export = !string.IsNullOrWhiteSpace(otlpEndpoint);
        if (export)
        {
            _ = new Uri(otlpEndpoint!, UriKind.Absolute);
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddSource(CoordinationTelemetry.SourceName, PersistenceSource, WorkSource);
                if (export)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(CoordinationTelemetry.SourceName, PersistenceSource, WorkSource);
                if (export)
                {
                    metrics.AddOtlpExporter();
                }
            });
    }
}
