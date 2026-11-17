using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Fixture;

/// <summary>The fixtures behave as the tests that use them assume.</summary>
public class FixtureSmokeTests
{
    [Fact]
    public void TheSkipOrFailRuleFollowsCi()
    {
        Assert.Equal(RequireDocker.Outcome.Run, RequireDocker.Decide(null, "true"));
        Assert.Equal(RequireDocker.Outcome.Skip, RequireDocker.Decide("no docker", null));
        Assert.Equal(RequireDocker.Outcome.Skip, RequireDocker.Decide("no docker", "false"));
        Assert.Equal(RequireDocker.Outcome.Fail, RequireDocker.Decide("no docker", "true"));
        Assert.Equal(RequireDocker.Outcome.Fail, RequireDocker.Decide("no docker", "TRUE"));
    }

    [Fact]
    public async Task DockerAnswersOrTheTestSkips()
    {
        await RequireDocker.ThatAsync();

        var result = await DockerCli.RunAsync(["version", "--format", "{{.Server.Version}}"], TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.StandardOutput.Trim());
    }

    [Fact]
    public async Task AHeldProbeWaitsForItsRelease()
    {
        var probe = new StubPersistenceProbe(PersistenceHealthResult.Unhealthy(PersistenceHealthReason.Timeout));
        probe.Hold();

        var call = probe.CheckAsync(TestContext.Current.CancellationToken).AsTask();
        await probe.WhenCalledAsync(1);
        Assert.False(call.IsCompleted);

        probe.Release();
        Assert.Equal(PersistenceHealthReason.Timeout, (await call).Reason);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task AHeldCallEndsWithItsCallersToken()
    {
        var probe = new StubCoordinationProbe();
        probe.Hold();
        using var cancel = new CancellationTokenSource();

        var call = probe.CheckAsync(cancel.Token).AsTask();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public void TheSingleInstanceStubCountsReads()
    {
        var status = new StubSingleInstanceStatus(SingleInstanceState.Held);

        Assert.Equal(SingleInstanceState.Held, status.State);
        status.Value = SingleInstanceState.Lost;
        Assert.Equal(SingleInstanceState.Lost, status.State);
        Assert.Equal(2, status.Reads);
    }

    [Fact]
    public void TheTestFileSystemMatchesCaseAndRecordsOpens()
    {
        var files = new TestFileSystem().WithFile("/config/iskender.json", "{}").WithUnreadableFile("/config/locked.json");

        Assert.True(files.DirectoryExists("/config"));
        Assert.True(files.FileExists("/config/iskender.json"));
        Assert.False(files.FileExists("/config/Iskender.json"));
        using (var stream = files.OpenRead("/config/iskender.json"))
        {
            Assert.Equal(2, stream.Length);
        }

        Assert.Throws<UnauthorizedAccessException>(() => files.OpenRead("/config/locked.json"));
        Assert.Equal(["/config/iskender.json", "/config/locked.json"], files.Opened);
    }

    [Fact]
    public void TheTestHostChoosesTwoPortsAndWritesItsDocument()
    {
        using var host = new ContainerTestHost().WithDocument();

        Assert.NotEqual(host.TrafficPort, host.HealthPort);
        Assert.True(host.Files.FileExists("/config/iskender.json"));
        Assert.Equal(ContainerTestHost.ConfigDirectory, host.Environment["ISKENDER_CONFIG_DIRECTORY"]);
    }
}
