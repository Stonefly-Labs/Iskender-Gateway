using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.CommandLine;

/// <summary>
/// Serving commands (ISK-0009 T047, research R11, container contract Command line rule 1): <c>work</c> runs the serve path
/// with the worker's plane and composition and only the health listener, takes no further arguments, and runs its check
/// after validation and before anything binds, so a rejected setting exits 2.
/// </summary>
public class ServingCommandTests
{
    [Fact]
    public async Task WorkServesTheWorkerPlaneWithOnlyTheHealthListener()
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"");
        using var client = new HttpClient();
        CompositionRoot? root = null;
        var checkedBeforeBinding = false;
        var definition = TestPlanes.WithWorker(host, check: provider =>
        {
            root = provider.GetRequiredService<CompositionRoot>();
            checkedBeforeBinding = !ContainerTestHost.IsListeningAsync(host.HealthPort).GetAwaiter().GetResult();
        });

        var run = Task.Run(() => host.RunAsync(definition, "work"));
        await ContainerTestHost.WhenListeningAsync(host.HealthPort, run);
        var (status, _) = await host.WhenStartedAsync(client);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(await ContainerTestHost.IsListeningAsync(host.TrafficPort));
        Assert.Equal(CompositionPlane.Worker, root!.Plane);
        Assert.True(checkedBeforeBinding);
        Assert.Equal("worker", host.Log.Only(7001).Value("Plane"));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
        Assert.Equal("worker", host.Log.Only(7005).Value("Plane"));
    }

    [Theory]
    [InlineData("work", "extra")]
    [InlineData("work", "--urls", "http://*:5000")]
    public async Task WorkFollowedByAnyArgumentIsAUsageErrorBeforeAnyFileIsRead(params string[] args)
    {
        using var host = new ContainerTestHost().WithDocument();
        var composed = false;

        var exitCode = await host.RunAsync(TestPlanes.WithWorker(host, _ => composed = true), args);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains("|work", host.StandardError.ToString(), StringComparison.Ordinal);
        Assert.False(composed);
        Assert.Empty(host.Files.Opened);
        Assert.Empty(host.Log.Entries);
    }

    [Fact]
    public async Task ASettingTheCheckRejectsExits2BeforeAnythingBinds()
    {
        using var host = new ContainerTestHost().WithDocument();
        var definition = TestPlanes.WithWorker(host, check: _ => throw new GatewayException(ErrorCodes.ConfigFieldOutOfRange));

        var exitCode = await host.RunAsync(definition, "work");

        Assert.Equal(ExitCodes.ConfigurationRejected, exitCode);
        Assert.Equal(ErrorCodes.ConfigFieldOutOfRange.Id, host.Log.Only(7002).Value("Code"));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.HealthPort));
    }

    [Fact]
    public async Task TheServingControlPlaneDoesNotRunTheWorkersCheck()
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"");
        using var client = new HttpClient();
        var ran = false;

        var run = await host.StartAsync(TestPlanes.WithWorker(host, check: _ => ran = true));
        await host.WhenStartedAsync(client);
        host.Stop();

        Assert.Equal(ExitCodes.Success, await run);
        Assert.False(ran);
        Assert.Equal("control_plane", host.Log.Only(7001).Value("Plane"));
    }
}
