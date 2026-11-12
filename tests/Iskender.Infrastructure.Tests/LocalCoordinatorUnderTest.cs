using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests;

/// <summary>
/// The local substitutes under the conformance suites: one process's state, shared by every client index, on a
/// <see cref="ManualTimeProvider"/> that <see cref="PassTimeAsync"/> moves forward. Events go to a capturing logger.
/// </summary>
internal sealed class LocalCoordinatorUnderTest : ICoordinatorUnderTest
{
    public LocalCoordinatorUnderTest(string sectionFields = "")
    {
        CoordinationSettings = ReadSettings(sectionFields);
        Leases = new LocalLeaseCoordinator(CoordinationSettings, Logger, Clock);
        Atomic = new LocalAtomicOperations(CoordinationSettings, Clock);
        Cache = new LocalVersionedCache(CoordinationSettings, Logger, Clock);
        Settings = new ConformanceSettings(
            CoordinationSettings.MinimumLeaseDuration,
            CoordinationSettings.MaximumLeaseDuration,
            CoordinationSettings.SafetyMargin,
            CoordinationSettings.MaximumWait,
            CoordinationSettings.MaximumExpiry,
            CoordinationSettings.MaximumValueBytes,
            CoordinationSettings.OperationTimeout);
    }

    public ManualTimeProvider Clock { get; } = new();

    public CapturingDiagnostics.Logger Logger { get; } = new();

    public CoordinationSettings CoordinationSettings { get; }

    public LocalLeaseCoordinator Leases { get; }

    public LocalAtomicOperations Atomic { get; }

    public CoordinatorKind Kind => CoordinatorKind.Local;

    public CoordinatorCapabilities Capabilities => CoordinatorCapabilities.Local;

    public bool IsDistributed => false;

    public ConformanceSettings Settings { get; }

    public ConformanceScopes Scopes { get; } = new();

    public void Require()
    {
    }

    public LocalVersionedCache Cache { get; }

    public CoordinatorClient CreateClient(int index) => new(Atomic, Leases, Cache);

    public async ValueTask PassTimeAsync(TimeSpan duration)
    {
        Clock.Advance(duration);

        // Let continuations of the timers that fired run before the suite looks at their tasks.
        await Task.Delay(TimeSpan.FromMilliseconds(1), CancellationToken.None);
    }

    /// <summary>Settings read from a validated <c>coordination</c> section with <paramref name="sectionFields"/>.</summary>
    public static CoordinationSettings ReadSettings(string sectionFields = "")
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("coordination.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"coordination\":{" + sectionFields + "}}}"),
            environmentName: "development"));
        Assert.True(report.IsValid, report.ToString());
        return CoordinationSettings.From(report.Snapshot!.GetSection(CoordinationSectionSchema.Schema));
    }
}
