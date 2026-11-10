using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Diagnostics;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.PostgreSql.Tests.Diagnostics;

public class PersistenceLogTests
{
    /// <summary>The contract's allowed fields: module, migration version, operation kind, outcome, failure class, reason code, durations.</summary>
    internal static readonly HashSet<string> AllowedFields = ["Module", "Version", "Operation", "Outcome", "FailureClass", "Reason", "PreviousReason", "SqlStateClass", "DurationMs"];

    public static TheoryData<int, string, LogLevel> Events() => new()
    {
        { 4001, "PersistenceConnectionFailed", LogLevel.Warning },
        { 4002, "UnitOfWorkRolledBack", LogLevel.Debug },
        { 4003, "UnitOfWorkFaulted", LogLevel.Warning },
        { 4004, "UnitOfWorkCommitOutcomeUnknown", LogLevel.Error },
        { 4006, "AdvisoryLockTimedOut", LogLevel.Warning },
        { 4007, "MigrationStarted", LogLevel.Information },
        { 4008, "MigrationLockWaiting", LogLevel.Information },
        { 4009, "MigrationStepApplied", LogLevel.Information },
        { 4010, "MigrationStepFailed", LogLevel.Error },
        { 4011, "MigrationCompleted", LogLevel.Information },
        { 4012, "MigrationRefused", LogLevel.Error },
        { 4013, "PersistenceHealthChanged", LogLevel.Information },
        { 4014, "PersistenceUnexpectedFailure", LogLevel.Error },
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void EachEventHasItsIdNameLevelAndOnlyAllowedFields(int id, string name, LogLevel level)
    {
        var logger = new CapturingDiagnostics.Logger();

        WriteEvery(logger);

        var logged = Assert.Single(logger.Events, logged => logged.Id.Id == id);
        Assert.Equal(name, logged.Id.Name);
        Assert.Equal(level, logged.Level);
        Assert.All(logged.Fields, field => Assert.Contains(field.Key, AllowedFields));
        Assert.Null(logged.Exception);
    }

    [Fact]
    public void ThirteenEventsAreDefinedHereAndNoneRepeats()
    {
        var logger = new CapturingDiagnostics.Logger();

        WriteEvery(logger);

        Assert.Equal(13, logger.Events.Select(logged => logged.Id.Id).Distinct().Count());
        Assert.Equal(logger.Events.Count, logger.Events.Select(logged => logged.Id.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(PersistenceFailureClass.TransientConflict, "transient_conflict")]
    [InlineData(PersistenceFailureClass.ConcurrencyConflict, "concurrency_conflict")]
    [InlineData(PersistenceFailureClass.UniqueConflict, "unique_conflict")]
    [InlineData(PersistenceFailureClass.Unavailable, "unavailable")]
    [InlineData(PersistenceFailureClass.Timeout, "timeout")]
    [InlineData(PersistenceFailureClass.Cancelled, "cancelled")]
    [InlineData(PersistenceFailureClass.AuthenticationFailed, "authentication_failed")]
    [InlineData(PersistenceFailureClass.ScopeViolation, "scope_violation")]
    [InlineData(PersistenceFailureClass.Unexpected, "unexpected")]
    public void FailureClassesAreWrittenInSnakeCase(PersistenceFailureClass failureClass, string text)
    {
        Assert.Equal(text, PersistenceLog.Text(failureClass));
    }

    private static void WriteEvery(ILogger logger)
    {
        var migration = new MigrationIdentity("alpha", 2);
        PersistenceLog.LogConnectionFailed(logger, "alpha", "widgets.read", PersistenceFailureClass.Unavailable);
        PersistenceLog.LogRolledBack(logger, "alpha", "widgets.read", "disposed");
        PersistenceLog.LogFaulted(logger, "alpha", "widgets.read", PersistenceFailureClass.Timeout);
        PersistenceLog.LogCommitOutcomeUnknown(logger, "alpha", "widgets.read");
        PersistenceLog.LogLockTimedOut(logger, "alpha", "widgets.read", TimeSpan.FromMilliseconds(250));
        PersistenceLog.LogMigrationStarted(logger, "apply");
        PersistenceLog.LogMigrationLockWaiting(logger, "apply", TimeSpan.FromSeconds(1));
        PersistenceLog.LogMigrationStepApplied(logger, migration, "apply", TimeSpan.FromMilliseconds(12));
        PersistenceLog.LogMigrationStepFailed(logger, migration, PersistenceFailureClass.Unexpected, "forward_failed");
        PersistenceLog.LogMigrationCompleted(logger, "apply", "succeeded", TimeSpan.FromSeconds(2));
        PersistenceLog.LogMigrationRefused(logger, "persistence.schema_incompatible", "dependency_cycle", module: null, version: null);
        PersistenceLog.LogHealthChanged(logger, PersistenceHealthResult.Unhealthy(PersistenceHealthReason.SchemaBehind), PersistenceHealthReason.Ok);
        PersistenceLog.LogUnexpectedFailure(logger, "alpha", "widgets.read", "42");
    }
}
