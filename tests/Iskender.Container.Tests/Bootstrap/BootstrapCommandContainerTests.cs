using System.Text.Json.Nodes;
using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Errors;
using Iskender.Infrastructure.Container;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;

namespace Iskender.Container.Tests.Bootstrap;

/// <summary>
/// The <c>bootstrap</c> command on the images (ISK-0010 T039, container contract Test fixtures rule 8, Bootstrap command
/// rules 1 to 5, Diagnostics rule 9): until ISK-0021 provides a credential provider, <c>bootstrap issue</c> and
/// <c>bootstrap recover</c> exit 5 with <c>persistence.unavailable</c>, print nothing to standard output, and write event
/// 7008 to standard error; <c>bootstrap</c> on the data plane, and with a missing, unknown, or extra argument, exits 64;
/// and a control plane with an admin route that makes no declaration exits 10 and binds no listener.
/// </summary>
[Trait("Category", "Compose")]
public class BootstrapCommandContainerTests(ImageBuild build)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("issue")]
    [InlineData("recover")]
    public async Task EachOperationExits5WithPersistenceUnavailableAndNothingOnStandardOutput(string operation)
    {
        var image = (await build.GetAsync()).ControlPlane;
        using var configuration = MountedConfiguration.Unreachable();

        var result = await RunAsync(image, configuration.Mount, ["bootstrap", operation]);

        Assert.Equal(ExitCodes.MigrationFailed, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        var finished = Assert.Single(Events(result.StandardError), logged => logged.Id == 7008).State;
        Assert.Equal(
            (operation, "failed", ErrorCodes.PersistenceUnavailable.Id),
            (finished["Operation"]!.GetValue<string>(), finished["Outcome"]!.GetValue<string>(), finished["Code"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("data_plane", "bootstrap issue")]
    [InlineData("control_plane", "bootstrap")]
    [InlineData("control_plane", "bootstrap rotate")]
    [InlineData("control_plane", "bootstrap issue extra")]
    public async Task ABootstrapUsageErrorExits64(string plane, string command)
    {
        var image = (await build.GetAsync()).For(plane);
        using var configuration = MountedConfiguration.Unreachable();

        var result = await RunAsync(image, configuration.Mount, command.Split(' '));

        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.DoesNotContain(Events(result.StandardError), logged => logged.Id == 7008);
    }

    // A run stopped while it runs re-throws the cancellation, and the entry point exits 1; the shipped definition still
    // writes 7008, failed with no code, since its arguments parsed (Bootstrap command rule 6, Diagnostics rule 9;
    // CONV-0010-009).
    [Theory]
    [InlineData("issue")]
    [InlineData("recover")]
    public async Task ACancelledRunWhoseArgumentsParsedStillWritesEvent7008(string operation)
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        host.Stop();

        var exitCode = await host.RunAsync(Iskender.ControlPlane.Host.ControlPlaneDefinition.Create(), "bootstrap", operation);

        Assert.Equal(ExitCodes.Unexpected, exitCode);
        var finished = host.Log.Only(7008);
        Assert.Equal((operation, "failed", "-"), (finished.Value("Operation"), finished.Value("Outcome"), finished.Value("Code")));
    }

    [Fact]
    public async Task AnAdminRouteWithoutADeclarationExits10AndBindsNoListener()
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        var plane = PlaneDefinitions.ControlPlane(host.AddStubs);
        var undeclared = plane with
        {
            MapTraffic = app => app.MapIskenderOperatorSecurity("/admin/v1").MapGet("/undeclared", () => "undeclared"),
        };

        var exitCode = await host.RunAsync(undeclared);

        Assert.Equal(ExitCodes.RouteUndeclared, exitCode);
        Assert.Equal((ErrorCodes.OperatorRouteUndeclared.Id, "10"), (host.Log.Only(7002).Value("Code"), host.Log.Only(7002).Value("ExitCode")));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.TrafficPort));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.HealthPort));
    }

    private static Task<DockerResult> RunAsync(string image, IReadOnlyList<string> options, IReadOnlyList<string> command) =>
        DockerCli.RunAsync(["run", "--rm", "--read-only", "--tmpfs", "/tmp", .. options, image, .. command], TimeSpan.FromMinutes(2), Token);

    // Each JSON line's event ID and state, in the order written.
    private static List<(int Id, JsonObject State)> Events(string output) =>
        [.. output.Split('\n')
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(entry => entry["EventId"] is not null && entry["State"] is JsonObject)
            .Select(entry => (entry["EventId"]!.GetValue<int>(), entry["State"]!.AsObject()))];
}
