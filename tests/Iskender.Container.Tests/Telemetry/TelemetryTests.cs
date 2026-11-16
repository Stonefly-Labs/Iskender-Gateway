using System.Diagnostics;
using Iskender.Infrastructure.Container.Telemetry;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Iskender.Container.Tests.Telemetry;

/// <summary>Container contract, Telemetry rules 1 and 2 (research R15): the subscription, the service name, and the exporter only when its endpoint is set.</summary>
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
    public void TheResourceNamesTheService()
    {
        using var provider = Provider("iskender-control-plane");

        var resource = provider.GetRequiredService<TracerProvider>().GetResource();

        Assert.Contains(resource.Attributes, attribute => attribute.Key == "service.name" && (string)attribute.Value == "iskender-control-plane");
    }

    [Theory]
    [InlineData(CoordinationTelemetry.SourceName)]
    [InlineData(ContainerTelemetry.PersistenceSource)]
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

    [Fact]
    public void ThePersistenceLiteralIsTheAdaptersDiagnosticsName() =>
        Assert.Equal(
            typeof(PostgreSqlServiceCollectionExtensions).Assembly.GetType("Iskender.Infrastructure.PostgreSql.PostgreSqlAdapter", throwOnError: true)!.GetField("DiagnosticsName")!.GetRawConstantValue(),
            ContainerTelemetry.PersistenceSource);

    private static ServiceProvider Provider(string serviceName)
    {
        var services = new ServiceCollection();
        ContainerTelemetry.AddIskenderContainerTelemetry(services, serviceName, otlpEndpoint: null);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static bool IsOtlp(ServiceDescriptor descriptor) =>
        Names(descriptor.ServiceType).Concat(descriptor.ImplementationType is { } type ? Names(type) : []).Any(name => name.Contains("Otlp", StringComparison.Ordinal));

    private static IEnumerable<string> Names(Type type) => [type.FullName ?? type.Name, .. type.GenericTypeArguments.SelectMany(Names)];
}
