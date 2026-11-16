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

    /// <summary>The variable that, set, adds the OTLP exporter.</summary>
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Subscribes to <c>Iskender.Coordination</c> and <c>Iskender.Persistence</c> with resource <c>service.name</c>
    /// <paramref name="serviceName"/>, and adds the OTLP exporter for traces and metrics only when
    /// <paramref name="otlpEndpoint"/> is set, so nothing is exported otherwise.
    /// </summary>
    public static void AddIskenderContainerTelemetry(IServiceCollection services, string serviceName, string? otlpEndpoint)
    {
        var endpoint = string.IsNullOrWhiteSpace(otlpEndpoint) ? null : new Uri(otlpEndpoint, UriKind.Absolute);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddSource(CoordinationTelemetry.SourceName, PersistenceSource);
                if (endpoint is not null)
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(CoordinationTelemetry.SourceName, PersistenceSource);
                if (endpoint is not null)
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = endpoint);
                }
            });
    }
}
