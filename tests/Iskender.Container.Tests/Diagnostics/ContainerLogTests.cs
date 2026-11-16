using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Container.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Iskender.Container.Tests.Diagnostics;

/// <summary>Container contract, Diagnostics: each event's ID, name, level, and template, and its rules 1 to 5 and 7.</summary>
public class ContainerLogTests
{
    public static TheoryData<int, string, LogLevel, string> Events => new()
    {
        { 7001, "ContainerStarting", LogLevel.Information, "Iskender container starting: plane {Plane}, release {Release}." },
        { 7002, "ContainerStartRefused", LogLevel.Error, "Iskender container start refused: code {Code}, exit code {ExitCode}, feature {Feature}, module {Module}, reason {Reason}." },
        { 7003, "ContainerReadinessChanged", LogLevel.Information, "Iskender readiness changed: check {Check}, from {From} to {To}." },
        { 7004, "ContainerStopRequested", LogLevel.Information, "Iskender container stop requested: plane {Plane}." },
        { 7005, "ContainerStopped", LogLevel.Information, "Iskender container stopped: plane {Plane}, exit code {ExitCode}, failure {Failure}." },
        { 7006, "ContainerMigrationFinished", LogLevel.Information, "Iskender migration command finished: operation {Operation}, outcome {Outcome}, count {Count}, code {Code}." },
        { 7007, "ContainerReadinessCheckFailed", LogLevel.Warning, "Iskender readiness check failed: check {Check}, failure {Failure}." },
    };

    private static void WriteEach(ILogger logger)
    {
        ContainerLog.LogStarting(logger, "data_plane", "1.0.0");
        ContainerLog.LogStartRefused(logger, "hosting.configuration_rejected", 2);
        ContainerLog.LogReadinessChanged(logger, "persistence", null, "unreachable");
        ContainerLog.LogStopRequested(logger, "data_plane");
        ContainerLog.LogStopped(logger, "data_plane", 0);
        ContainerLog.LogMigrationFinished(logger, "apply", "succeeded", 3);
        ContainerLog.LogReadinessCheckFailed(logger, "capability", nameof(InvalidOperationException));
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void EachEventHasItsIdNameLevelAndTemplate(int id, string name, LogLevel level, string template)
    {
        using var log = new CapturedLog();
        WriteEach(log.CreateLogger(ContainerLog.Category));

        var entry = log.Only(id);

        Assert.Equal("Iskender.Container", entry.Category);
        Assert.Equal(name, entry.Event.Name);
        Assert.Equal(level, entry.Level);
        Assert.Equal(template, entry.Value("{OriginalFormat}"));
    }

    [Fact]
    public void AbsentValuesAreWrittenAsADash()
    {
        using var log = new CapturedLog();
        var logger = log.CreateLogger(ContainerLog.Category);

        ContainerLog.LogStarting(logger, "control_plane", null);
        ContainerLog.LogStartRefused(logger, "hosting.registration_missing", 3);
        ContainerLog.LogReadinessChanged(logger, "lifecycle", null, "starting");
        ContainerLog.LogStopped(logger, "control_plane", 0);

        Assert.Equal("-", log.Only(7001).Value("Release"));
        var refused = log.Only(7002);
        Assert.Equal(["-", "-", "-"], [refused.Value("Feature"), refused.Value("Module"), refused.Value("Reason")]);
        Assert.Equal("3", refused.Value("ExitCode"));
        Assert.Equal("-", log.Only(7003).Value("From"));
        Assert.Equal("-", log.Only(7005).Value("Failure"));
    }

    [Fact]
    public void AMigrationFailureNamesItsCodeAndASuccessADash()
    {
        using var log = new CapturedLog();
        var logger = log.CreateLogger(ContainerLog.Category);

        ContainerLog.LogMigrationFinished(logger, "apply", "failed", 0, "persistence.migration_locked");
        ContainerLog.LogMigrationFinished(logger, "status", "current", 0);

        Assert.Equal(["persistence.migration_locked", "-"], log.ById(7006).Select(entry => entry.Value("Code")));
    }

    [Fact]
    public void ARefusedFeatureIsNamed()
    {
        using var log = new CapturedLog();

        ContainerLog.LogStartRefused(log.CreateLogger(ContainerLog.Category), "hosting.feature_refused", 4, "auth", "identity", "missing_dependency");

        var entry = log.Only(7002);
        Assert.Equal(["auth", "identity", "missing_dependency"], [entry.Value("Feature"), entry.Value("Module"), entry.Value("Reason")]);
    }

    [Theory]
    [InlineData(0, LogLevel.Information)]
    [InlineData(1, LogLevel.Error)]
    [InlineData(2, LogLevel.Error)]
    [InlineData(64, LogLevel.Error)]
    public void StoppedIsAnErrorForANonZeroExitCode(int exitCode, LogLevel level)
    {
        using var log = new CapturedLog();

        ContainerLog.LogStopped(log.CreateLogger(ContainerLog.Category), "data_plane", exitCode, exitCode == 1 ? nameof(InvalidOperationException) : null);

        var entry = log.Only(7005);
        Assert.Equal(level, entry.Level);
        Assert.Equal("ContainerStopped", entry.Event.Name);
        Assert.Equal(exitCode == 1 ? "InvalidOperationException" : "-", entry.Value("Failure"));
    }
}
