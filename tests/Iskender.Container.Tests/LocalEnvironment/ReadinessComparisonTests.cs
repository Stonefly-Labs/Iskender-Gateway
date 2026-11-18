using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// User Story 2, acceptance scenario 6, and FR-019 (local-environment contract, Smoke assertions 3; research R13):
/// <c>lib/readiness.sh</c> accepts only status 503 with the exact expected body, and names each check whose reason
/// differs, the status, and a non-empty refused list. Needs <c>bash</c> and <c>git</c>, and no container runtime.
/// </summary>
public sealed class ReadinessComparisonTests(SharedCheckout copy) : IClassFixture<SharedCheckout>
{
    [Fact]
    public async Task TheExpectedAnswerIsAccepted()
    {
        var result = await RunAsync("isk_ready_compare", "503", RecordedReadiness.Expected);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public async Task TheLibraryHoldsTheRecordedBody()
    {
        var result = await RunAsync("isk_ready_expected");

        Assert.Equal(RecordedReadiness.Expected, result.StandardOutput);
    }

    // ISK-0009 T058: the worker's answer is its own, and neither answer passes for the other.
    [Fact]
    public async Task TheLibraryHoldsTheWorkersRecordedBodyAndThePlanesUnchanged()
    {
        Assert.Equal(RecordedReadiness.Worker, (await RunAsync("isk_ready_expected", "worker")).StandardOutput);
        Assert.Equal(RecordedReadiness.Expected, (await RunAsync("isk_ready_expected", "")).StandardOutput);
    }

    [Fact]
    public async Task TheWorkersAnswerIsAcceptedOnlyForTheWorker()
    {
        var worker = await RunAsync("isk_ready_compare", "503", RecordedReadiness.Worker, "worker");
        var planeAsWorker = await RunAsync("isk_ready_compare", "503", RecordedReadiness.Expected, "worker");
        var workerAsPlane = await RunAsync("isk_ready_compare", "503", RecordedReadiness.Worker);

        Assert.Equal((0, ""), (worker.ExitCode, worker.StandardOutput));
        Assert.Equal((1, "coordination: expected authentication_failed, found ok\n"), (planeAsWorker.ExitCode, planeAsWorker.StandardOutput));
        Assert.Equal((1, "coordination: expected ok, found authentication_failed\n"), (workerAsPlane.ExitCode, workerAsPlane.StandardOutput));
    }

    [Fact]
    public async Task PersistenceOkFailsNamingPersistence()
    {
        var result = await RunAsync("isk_ready_compare", "200", RecordedReadiness.PersistenceOk);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("persistence: expected authentication_failed, found ok", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("status: expected 503, found 200", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CoordinationUnreachableFailsNamingCoordination()
    {
        var result = await RunAsync("isk_ready_compare", "503", RecordedReadiness.CoordinationUnreachable);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("coordination: expected ok, found unreachable\n", result.StandardOutput);
    }

    [Fact]
    public async Task TheExpectedBodyWithAnotherStatusFailsNamingTheStatus()
    {
        var result = await RunAsync("isk_ready_compare", "200", RecordedReadiness.Expected);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("status: expected 503, found 200\n", result.StandardOutput);
    }

    [Fact]
    public async Task ANonEmptyRefusedListFails()
    {
        var result = await RunAsync("isk_ready_compare", "503", RecordedReadiness.Refused);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("refused: ", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoAnswerFailsNamingEveryCheck()
    {
        var result = await RunAsync("isk_ready_compare", "000", "");

        Assert.Equal(1, result.ExitCode);
        foreach (var check in new[] { "lifecycle", "persistence", "coordination", "single_instance", "capability" })
        {
            Assert.Contains($"{check}: expected ", result.StandardOutput, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StartingIsRecognised()
    {
        Assert.Equal(0, (await RunAsync("isk_ready_starting", RecordedReadiness.Starting)).ExitCode);
        Assert.Equal(1, (await RunAsync("isk_ready_starting", RecordedReadiness.Expected)).ExitCode);
    }

    private async Task<DockerResult> RunAsync(params string[] call)
    {
        var checkout = await copy.GetAsync();
        var library = Path.Combine(checkout.Root, "local", "scripts", "lib", "readiness.sh");
        return await DockerCli.RunProcessAsync(
            RequireLocalTools.Bash,
            ["-c", $"set -euo pipefail; . '{library}'; \"$@\"", "readiness", .. call],
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
    }
}
