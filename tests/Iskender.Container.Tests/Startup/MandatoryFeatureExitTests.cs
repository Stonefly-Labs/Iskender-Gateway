using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Startup;

/// <summary>
/// User Story 2, acceptance scenario 8, and SC-004's container half: a refused feature with <c>MandatoryPath</c> exits 4
/// with events 6004 and 7002, and without it the process serves, leaves the capability check ready, lists the feature,
/// and refuses its calls (container contract, Start-up rules 3 and 4). <c>single_instance_violated</c> needs PostgreSQL, so it is in
/// <see cref="MandatoryFeatureSingleInstanceExitTests"/>.
/// </summary>
public class MandatoryFeatureExitTests
{
    internal const string Feature = "fixture.guarded";

    internal const string Module = "fixture";

    private static readonly CoordinationName Resource = new("fixture", "resource");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Reasons() =>
        ["degrade_not_permitted", "coordinator_not_configured", "local_in_multi_replica", "unsupported_consistency"];

    // Each reason, the coordination section that produces it, the declaration, and, without MandatoryPath, the reason the
    // feature is listed with and the checks that are not ready. Only a mandatory path refuses a feature that degrades, so
    // without it that feature reaches the next rule (SC-004, maintainer decision, 2026-11-16, implement); a selected
    // coordinator with no settings is coordination not_configured and unhealthy, which is not ready through the
    // coordination check, never the capability check (container contract, Readiness rules 4 and 6).
    private static readonly Dictionary<string, Case> Cases = new(StringComparer.Ordinal)
    {
        ["degrade_not_permitted"] = new("{}", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Strict, Degrades: true, "local_in_multi_replica", NotReady: []),
        ["coordinator_not_configured"] = new("{\"leases\":{\"coordinator\":\"redis\"}}", CoordinationPrimitive.Leases, ConsistencyLevel.Leased, Degrades: false, "coordinator_not_configured", NotReady: ["coordination"]),
        ["local_in_multi_replica"] = new("{\"topology\":\"multi_replica\"}", CoordinationPrimitive.Leases, ConsistencyLevel.Leased, Degrades: false, "local_in_multi_replica", NotReady: []),
        ["unsupported_consistency"] = new("{\"topology\":\"single_instance\"}", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Strict, Degrades: false, "unsupported_consistency", NotReady: []),
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task AMandatoryRefusedFeatureExits4WithEvents6004And7002(string reason)
    {
        var definition = Cases[reason];
        using var host = new ContainerTestHost().WithDocument(sections: "\"coordination\":" + definition.Coordination);

        var exitCode = await host.RunAsync(Plane(host, definition, mandatory: true));

        Assert.Equal(ExitCodes.MandatoryFeatureRefused, exitCode);
        AssertRefusal(host, reason);
        Assert.False(await ContainerTestHost.IsListeningAsync(host.HealthPort));
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task WithoutMandatoryPathTheProcessServesListsTheFeatureAndRefusesItsCalls(string reason)
    {
        var definition = Cases[reason];
        using var host = new ContainerTestHost().WithDocument(sections: "\"coordination\":" + definition.Coordination);
        var capture = new ProviderCapture();
        using var client = new HttpClient();
        var run = await host.StartAsync(Plane(host, definition, mandatory: false, capture));

        var (status, body) = await host.WhenStartedAsync(client);

        Assert.Equal(definition.NotReady.Length == 0 ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, status);
        AssertListed(body, definition.Listed);
        Assert.Equal(definition.NotReady, NotReady(body));
        await AssertCallRefusedAsync(await capture.Provider, definition.Primitive);
        Assert.Empty(host.Log.ById(6004));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Fact]
    public async Task ACoordinatorSelectedWithNoRegistrationExits3()
    {
        using var host = new ContainerTestHost().WithDocument(sections: "\"coordination\":{\"leases\":{\"coordinator\":\"postgresql\"}}");

        var exitCode = await host.RunAsync(TestPlanes.ControlPlane(host, services => services.AddIskenderCoordination()));

        Assert.Equal(ExitCodes.RegistrationMissing, exitCode);
        var refused = host.Log.Only(7002);
        Assert.Equal(("hosting.registration_missing", "3"), (refused.Value("Code"), refused.Value("ExitCode")));
        Assert.Single(host.Log.ById(6003));
    }

    internal static void AssertRefusal(ContainerTestHost host, string reason)
    {
        var composition = host.Log.Only(6004);
        Assert.Equal((Feature, Module, reason), (composition.Value("Feature"), composition.Value("Module"), composition.Value("Reason")));
        var container = host.Log.Only(7002);
        Assert.Equal(
            ("coordination.capability_unsupported", "4", Feature, Module, reason),
            (container.Value("Code"), container.Value("ExitCode"), container.Value("Feature"), container.Value("Module"), container.Value("Reason")));
        Assert.Contains(host.Log.ById(5002), entry => entry.Value("Reason") == reason);
        Assert.Equal("4", host.Log.Only(7005).Value("ExitCode"));
    }

    internal static void AssertListed(System.Text.Json.JsonElement body, string reason)
    {
        Assert.Equal(("ready", "refused_features"), (ContainerTestHost.Status(body, "capability"), ContainerTestHost.Reason(body, "capability")));
        var refused = Assert.Single(body.GetProperty("refused").EnumerateArray());
        Assert.Equal(
            (Feature, Module, reason),
            (refused.GetProperty("feature").GetString(), refused.GetProperty("module").GetString(), refused.GetProperty("reason").GetString()));
    }

    /// <summary>The names of the checks a readiness body reports not ready, in the contract's order.</summary>
    internal static string?[] NotReady(System.Text.Json.JsonElement body) =>
        [.. body.GetProperty("checks").EnumerateArray()
            .Where(check => check.GetProperty("status").GetString() == "not_ready")
            .Select(check => check.GetProperty("name").GetString())];

    internal static async Task AssertCallRefusedAsync(IServiceProvider services, CoordinationPrimitive primitive)
    {
        var feature = services.GetRequiredService<CoordinationGate>().ForFeature(Feature);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
        {
            if (primitive == CoordinationPrimitive.Leases)
            {
                await feature.Leases.AcquireAsync(CoordinationScope.Operator, Resource, TimeSpan.FromSeconds(5), LeaseWait.TryOnly, Token);
            }
            else
            {
                await feature.Atomic.ReadAsync(CoordinationScope.Operator, Resource, Token);
            }
        });
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
    }

    internal static FeatureRequirement Guarded(CoordinationPrimitive primitive, ConsistencyLevel minimum, bool mandatory, bool degrades = false) =>
        new(
            Feature,
            Module,
            new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [primitive] = minimum },
            mandatory,
            degrades ? OutageBehavior.Degrade("fixture.guarded_degraded") : OutageBehavior.FailClosed);

    private static ContainerPlaneDefinition Plane(ContainerTestHost host, Case definition, bool mandatory, ProviderCapture? capture = null) =>
        TestPlanes.ControlPlane(host, services =>
        {
            services.AddIskenderCoordination();
            services.AddIskenderRedisCoordination();
            services.AddSingleton(Guarded(definition.Primitive, definition.Minimum, mandatory, definition.Degrades));
            capture?.AddTo(services);
        });

    private sealed record Case(string Coordination, CoordinationPrimitive Primitive, ConsistencyLevel Minimum, bool Degrades, string Listed, string[] NotReady);
}
