using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Readiness;

/// <summary>The capability check (container contract, Readiness rules 1 and 6).</summary>
public class CapabilityReasonTests
{
    private static FeatureRequirement Feature(string name, bool mandatory) =>
        new(name, "example", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Leased }, mandatory, OutageBehavior.FailClosed);

    [Fact]
    public async Task NoneRefusedIsReadyWithAnEmptyList()
    {
        using var host = new ContainerTestHost().WithDocument();
        var report = new CapabilityReport([], CoordinationTopology.SingleInstance, SingleInstanceState.NotApplicable);
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host, services => services.AddSingleton(report)));

        var (_, body) = await host.WhenStartedAsync(client);

        Assert.Equal("none_refused", ContainerTestHost.Reason(body, "capability"));
        Assert.Equal("ready", ContainerTestHost.Status(body, "capability"));
        Assert.Empty(body.GetProperty("refused").EnumerateArray());
        host.Stop();
        await run;
    }

    [Fact]
    public async Task RefusedFeaturesWithoutMandatoryPathAreListedInRegistrationOrderAndStayReady()
    {
        using var host = new ContainerTestHost().WithDocument();
        var report = new CapabilityReport(
            [
                FeatureCapability.Refuse(Feature("example.second", false), CapabilityReason.LocalInMultiReplica),
                FeatureCapability.Refuse(Feature("example.first", false), CapabilityReason.UnsupportedConsistency),
            ],
            CoordinationTopology.MultiReplica,
            SingleInstanceState.NotApplicable);
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host, services => services.AddSingleton(report)));

        var (_, body) = await host.WhenStartedAsync(client);

        Assert.Equal("refused_features", ContainerTestHost.Reason(body, "capability"));
        Assert.Equal("ready", ContainerTestHost.Status(body, "capability"));
        Assert.Equal(
            [("example.second", "example", "local_in_multi_replica"), ("example.first", "example", "unsupported_consistency")],
            body.GetProperty("refused").EnumerateArray().Select(item => (item.GetProperty("feature").GetString(), item.GetProperty("module").GetString(), item.GetProperty("reason").GetString())));
        host.Stop();
        await run;
    }
}
