using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Domain.Tests.Configuration;

namespace Iskender.Domain.Tests.Registry.Composition;

/// <summary>
/// The refused-feature check after the start-ups have run (hosting contract, Start-up rule 7; research R4): the first
/// refused feature with <c>MandatoryPath</c> fails the start, for each of the five reasons.
/// </summary>
public class MandatoryFeatureTests
{
    private static readonly CompositionModule Root = new("hosting", [TestSchemas.Alpha], []);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<CapabilityReason, string> Reasons => new()
    {
        { CapabilityReason.DegradeNotPermitted, "degrade_not_permitted" },
        { CapabilityReason.SingleInstanceViolated, "single_instance_violated" },
        { CapabilityReason.CoordinatorNotConfigured, "coordinator_not_configured" },
        { CapabilityReason.LocalInMultiReplica, "local_in_multi_replica" },
        { CapabilityReason.UnsupportedConsistency, "unsupported_consistency" },
    };

    private static FeatureRequirement Feature(string name, bool mandatory) =>
        new(name, "example", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Leased }, mandatory, OutageBehavior.FailClosed);

    private static (CompositionLifecycle Lifecycle, CompositionStatus Status, CapturingLoggerFactory Logs, List<string> Journal) Create(params FeatureCapability[] features)
    {
        var logs = new CapturingLoggerFactory();
        var status = new CompositionStatus();
        var journal = new List<string>();
        var configuration = new CompositionConfiguration(
            new(CompositionDocument.FromText("base.json", ConfigurationDocumentFormat.Json, "{}"), null, false, null),
            ConfigurationSchemaRegistry.Create([TestSchemas.Alpha]),
            logs);
        var report = new CapabilityReport(features, CoordinationTopology.MultiReplica, SingleInstanceState.NotApplicable);
        var lifecycle = new CompositionLifecycle(
            [Root],
            configuration,
            status,
            () => [new RecordingStartup("beta", journal), new RecordingStartup("alpha", journal)],
            logs,
            () => report);
        return (lifecycle, status, logs, journal);
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task AMandatoryRefusedFeatureFailsTheStartNamingItsFeatureModuleAndReason(CapabilityReason reason, string code)
    {
        var (lifecycle, status, logs, journal) = Create(FeatureCapability.Refuse(Feature("example.mandatory", true), reason));

        var failure = await Assert.ThrowsAsync<GatewayException>(() => lifecycle.StartAsync(Token));

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.Equal(
            [new("feature", "example.mandatory"), new("module", "example"), new("reason", code)],
            failure.Error!.Diagnostics.Values);
        var refused = Assert.Single(logs.Logger.Events, entry => entry.Id.Id == 6004);
        Assert.Equal("IskenderMandatoryFeatureRefused", refused.Id.Name);
        Assert.Equal(["start alpha", "start beta", "stop beta", "stop alpha"], journal);
        Assert.Equal(CompositionPhase.Starting, status.Phase);
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task ARefusedFeatureWithoutMandatoryPathChangesNothingAtStart(CapabilityReason reason, string code)
    {
        _ = code;
        var (lifecycle, status, logs, journal) = Create(FeatureCapability.Refuse(Feature("example.optional", false), reason));

        await lifecycle.StartAsync(Token);

        Assert.Equal(CompositionPhase.Started, status.Phase);
        Assert.DoesNotContain(logs.Logger.Events, entry => entry.Id.Id == 6004);
        Assert.Equal(["start alpha", "start beta"], journal);
    }

    [Fact]
    public async Task TheFirstMandatoryRefusalInRegistrationOrderIsTheOneNamed()
    {
        var (lifecycle, _, logs, _) = Create(
            FeatureCapability.Refuse(Feature("example.optional", false), CapabilityReason.LocalInMultiReplica),
            FeatureCapability.Refuse(Feature("example.second", true), CapabilityReason.DegradeNotPermitted),
            FeatureCapability.Refuse(Feature("example.third", true), CapabilityReason.UnsupportedConsistency));

        var failure = await Assert.ThrowsAsync<GatewayException>(() => lifecycle.StartAsync(Token));

        Assert.Contains(new KeyValuePair<string, string>("feature", "example.second"), failure.Error!.Diagnostics.Values);
        Assert.Single(logs.Logger.Events, entry => entry.Id.Id == 6004);
    }

    [Fact]
    public async Task WithNoCoordinationComposedTheStartSucceeds()
    {
        var logs = new CapturingLoggerFactory();
        var status = new CompositionStatus();
        using var configuration = new CompositionConfiguration(
            new(CompositionDocument.FromText("base.json", ConfigurationDocumentFormat.Json, "{}"), null, false, null),
            ConfigurationSchemaRegistry.Create([TestSchemas.Alpha]),
            logs);

        await new CompositionLifecycle([Root], configuration, status, () => [], logs, () => null).StartAsync(Token);

        Assert.Equal(CompositionPhase.Started, status.Phase);
    }

    [Fact]
    public async Task AfterARefusalTheHostsStopStopsNothingTwice()
    {
        var (lifecycle, status, _, journal) = Create(FeatureCapability.Refuse(Feature("example.mandatory", true), CapabilityReason.DegradeNotPermitted));
        await Assert.ThrowsAsync<GatewayException>(() => lifecycle.StartAsync(Token));

        await lifecycle.StoppedAsync(Token);

        Assert.Equal(2, journal.Count(entry => entry.StartsWith("stop", StringComparison.Ordinal)));
        Assert.Equal(CompositionPhase.Stopped, status.Phase);
    }
}
