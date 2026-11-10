using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Diagnostics;

/// <summary>
/// The adapter's activities and metrics, from <c>ActivitySource</c> and <c>Meter</c> named <c>Iskender.Persistence</c>
/// (FR-032, research R18). Tags and labels are only module, operation kind, outcome, and failure class, plus the migration
/// version on a migration step's activity. The retry counter lives with the retry helper in the ports, under the same
/// meter name. Npgsql exposes no pool statistics, so the connections gauge reports the connections held by open units of
/// work. Npgsql's own tracing and logging are never enabled, since they carry SQL and connection details. Owned by ISK-0004.
/// </summary>
internal static class PersistenceTelemetry
{
    public const string ModuleTag = "iskender.module";
    public const string OperationTag = "iskender.operation";
    public const string OutcomeTag = "iskender.outcome";
    public const string FailureClassTag = "iskender.failure_class";
    public const string VersionTag = "iskender.migration_version";

    public static readonly ActivitySource Source = new(PostgreSqlAdapter.DiagnosticsName);

    private static readonly Meter Meter = new(PostgreSqlAdapter.DiagnosticsName);
    private static readonly ConcurrentDictionary<string, long> HeldConnections = new(StringComparer.Ordinal);

    private static readonly Histogram<double> AcquireDuration = Meter.CreateHistogram<double>(
        "iskender.persistence.connection.acquire_duration", "s", "Time to acquire a pooled connection.");

    private static readonly Histogram<double> UnitDuration = Meter.CreateHistogram<double>(
        "iskender.persistence.unit_of_work.duration", "s", "Time from opening a unit of work to its end.");

    private static readonly Counter<long> Conflicts = Meter.CreateCounter<long>(
        "iskender.persistence.conflicts", "{conflict}", "Transient, concurrency, and unique conflicts.");

    static PersistenceTelemetry()
    {
        Meter.CreateObservableGauge(
            "iskender.persistence.pool.connections",
            () => HeldConnections.Select(entry => new Measurement<long>(entry.Value, new KeyValuePair<string, object?>(ModuleTag, entry.Key))),
            "{connection}",
            "Connections held by open units of work.");
    }

    public static Activity? StartUnit(string module, string operation) =>
        Source.StartActivity("persistence.unit_of_work", ActivityKind.Client, parentContext: default, [new(ModuleTag, module), new(OperationTag, operation)]);

    public static Activity? StartMigrationStep(MigrationIdentity migration, string operation) =>
        Source.StartActivity("persistence.migration_step", ActivityKind.Internal, parentContext: default,
            [new(ModuleTag, migration.Module), new(OperationTag, operation), new(VersionTag, migration.Version)]);

    /// <summary>Sets the outcome tag, and the failure class tag on a failure.</summary>
    public static void End(Activity? activity, string outcome, PersistenceFailureClass? failureClass = null)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(OutcomeTag, outcome);
        if (failureClass is { } value)
        {
            activity.SetTag(FailureClassTag, PersistenceLog.Text(value));
            activity.SetStatus(ActivityStatusCode.Error);
        }

        activity.Dispose();
    }

    public static void RecordAcquire(string module, string operation, TimeSpan duration, string outcome) =>
        AcquireDuration.Record(duration.TotalSeconds, new(ModuleTag, module), new(OperationTag, operation), new(OutcomeTag, outcome));

    public static void RecordUnit(string module, string operation, TimeSpan duration, string outcome) =>
        UnitDuration.Record(duration.TotalSeconds, new(ModuleTag, module), new(OperationTag, operation), new(OutcomeTag, outcome));

    public static void RecordConflict(string module, string operation, PersistenceFailureClass failureClass) =>
        Conflicts.Add(1, new(ModuleTag, module), new(OperationTag, operation), new(FailureClassTag, PersistenceLog.Text(failureClass)));

    public static void ConnectionHeld(string module) => HeldConnections.AddOrUpdate(module, 1, (_, count) => count + 1);

    public static void ConnectionReleased(string module) => HeldConnections.AddOrUpdate(module, 0, (_, count) => Math.Max(0, count - 1));
}
