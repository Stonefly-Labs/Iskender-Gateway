using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// User Story 2, acceptance scenario 7, and SC-004's embedded half: an embedded host refuses to start for a refused feature
/// with <c>MandatoryPath</c>, for each of the five reasons, and starts with the same feature without it (hosting contract,
/// Start-up rule 7; research R4). Each reason comes from configuration, as ISK-0005's refusal scenarios produce it.
/// <c>single_instance_violated</c> needs a lease held on PostgreSQL, so it is in
/// <see cref="MandatoryFeatureSingleInstanceTests"/>.
/// </summary>
public class MandatoryFeatureRefusalTests
{
    internal const string Feature = "fixture.guarded";

    internal const string Module = "fixture";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Reasons() =>
        ["degrade_not_permitted", "coordinator_not_configured", "local_in_multi_replica", "unsupported_consistency"];

    // Each reason, the coordination section that produces it, and the declaration: a primitive, its minimum, and whether
    // the feature degrades in an outage.
    private static readonly Dictionary<string, Case> Cases = new(StringComparer.Ordinal)
    {
        ["degrade_not_permitted"] = new("", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Strict, Degrades: true),
        ["coordinator_not_configured"] = new("\"leases\":{\"coordinator\":\"redis\"}", CoordinationPrimitive.Leases, ConsistencyLevel.Leased, Degrades: false),
        ["local_in_multi_replica"] = new("\"topology\":\"multi_replica\"", CoordinationPrimitive.Leases, ConsistencyLevel.Leased, Degrades: false),
        ["unsupported_consistency"] = new("\"topology\":\"single_instance\"", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Strict, Degrades: false),
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task AMandatoryRefusedFeatureFailsTheStartNamingItsFeatureModuleAndReason(string reason)
    {
        var logs = new CapturedLog();
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Logging.AddProvider(logs);
                Compose(builder, reason, mandatory: true);
            },
            ct: Token));

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
        Assert.Equal([new("feature", Feature), new("module", Module), new("reason", reason)], failure.Error!.Diagnostics.Values);
        var refused = logs.Only(6004);
        Assert.Equal((Feature, Module, reason), (refused.Value("Feature"), refused.Value("Module"), refused.Value("Reason")));
        Assert.Contains(logs.ById(5002), entry => entry.Value("Reason") == reason);
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task TheSameFeatureWithoutMandatoryPathStarts(string reason)
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => Compose(builder, reason, mandatory: false), ct: Token);

        Assert.Empty(host.Logs.ById(6004));
        var capability = host.Services.GetRequiredService<CapabilityReport>().Find(Feature)!;
        Assert.False(capability.Accepted);
        if (reason != "degrade_not_permitted")
        {
            // Only a mandatory path refuses a feature that degrades, so without it the check goes on to the next rule.
            Assert.Equal(reason, capability.Reason.ToCode());
        }
    }

    internal static FeatureRequirement Guarded(CoordinationPrimitive primitive, ConsistencyLevel minimum, bool mandatory, bool degrades = false) =>
        new(
            Feature,
            Module,
            new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [primitive] = minimum },
            mandatory,
            degrades ? OutageBehavior.Degrade("fixture.guarded_degraded") : OutageBehavior.FailClosed);

    private static void Compose(WebApplicationBuilder builder, string reason, bool mandatory)
    {
        var definition = Cases[reason];
        var document = "{\"iskender\":{\"coordination\":{" + definition.Coordination + "}}}";
        builder.Services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document));
        builder.Services.AddIskenderCoordination();
        builder.Services.AddIskenderRedisCoordination();
        builder.Services.AddSingleton(Guarded(definition.Primitive, definition.Minimum, mandatory, definition.Degrades));
    }

    private sealed record Case(string Coordination, CoordinationPrimitive Primitive, ConsistencyLevel Minimum, bool Degrades);
}
