using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.ComposeTests;

/// <summary>
/// User Story 4, acceptance scenarios 4 and 5, on the images: the data plane has no <c>migrate</c> command, and the
/// control plane's <c>migrate</c> against the Compose PostgreSQL exits 5 until ISK-0021 provides a credential provider
/// (container contract, Command line rule 3, Migration command rules 5 and 7). No event carries the failure's code, so
/// <c>MigrationCommandTests</c> checks that it is <c>persistence.unavailable</c> with no credential provider, as here.
/// </summary>
[Trait("Category", "Compose")]
public class MigrationImageTests(ImageBuild build)
{
    private const string Scenario = "database-running";

    [Fact]
    public async Task TheDataPlaneImageHasNoMigrateCommand()
    {
        await using var project = await ComposeProject.CreateAsync(await build.GetAsync());

        var result = await project.RunOnceAsync(Scenarios.Service(Scenario, "data-plane"), "migrate", "apply");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.DoesNotContain("\"EventId\":7006", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("\"EventId\":4007", result.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("status")]
    public async Task TheControlPlaneImageExits5FailedWithNoConnectionDetail(string operation)
    {
        await using var project = await ComposeProject.CreateAsync(await build.GetAsync());

        var result = await project.RunOnceAsync(Scenarios.Service(Scenario, "control-plane"), "migrate", operation);

        Assert.Equal(ExitCodes.MigrationFailed, result.ExitCode);
        // Standard error also carries docker compose's own progress lines, which name the services.
        var output = result.StandardOutput;
        var events = Scenarios.EventIds(result.StandardOutput);
        Assert.Equal(1, events.Count(id => id == 7006));
        Assert.Contains($"\"Operation\":\"{operation}\",\"Outcome\":\"failed\",\"Count\":0", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(events, id => id is >= 7001 and <= 7005);
        Assert.All(Scenarios.DocumentValues, value => Assert.DoesNotContain(value, output, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnusableRootExits2With6002Then7006AndAReverseOfZeroStepsStillExits64()
    {
        var image = (await build.GetAsync()).ControlPlane;
        string[] run = ["run", "--rm", "--read-only", "--tmpfs", "/tmp", "--env", "ISKENDER_CONFIG_DIRECTORY=/nowhere", image, "migrate"];

        var rejected = await DockerCli.RunAsync([.. run, "apply"], TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        var usage = await DockerCli.RunAsync([.. run, "reverse", "--steps", "0"], TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.ConfigurationRejected, rejected.ExitCode);
        Assert.Equal([6002, 7006], Scenarios.EventIds(rejected.StandardOutput));
        Assert.Contains("\"Operation\":\"apply\",\"Outcome\":\"failed\",\"Count\":0", rejected.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Usage, usage.ExitCode);
        Assert.Empty(Scenarios.EventIds(usage.StandardOutput));
    }

    [Fact]
    public async Task AReverseOfZeroStepsExits64()
    {
        await using var project = await ComposeProject.CreateAsync(await build.GetAsync());

        var result = await project.RunOnceAsync(Scenarios.Service(Scenario, "control-plane"), "migrate", "reverse", "--steps", "0");

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Empty(Scenarios.EventIds(result.StandardOutput));
    }
}
