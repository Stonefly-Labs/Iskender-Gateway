using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Errors;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Roots;

/// <summary>
/// ISK-0010 T037, spec Edge Cases: only the control plane registers the <c>operator</c> section, so a data-plane or
/// worker document holding one exits 2 with <c>config.namespace.unknown</c>, and the control plane accepts it.
/// </summary>
public class OperatorSectionRejectionTests
{
    private const string Operator = ",\"operator\":{}";

    private const string Leases = ",\"coordination\":{\"leases\":{\"coordinator\":\"postgresql\"}}";

    [Theory]
    [InlineData("data-plane")]
    [InlineData("worker")]
    public async Task AnOperatorSectionInADataPlaneOrWorkerDocumentExits2WithNamespaceUnknown(string process)
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence + Leases + Operator);

        var exitCode = process == "data-plane"
            ? await host.RunAsync(PlaneDefinitions.DataPlane(host.AddStubs))
            : await host.RunAsync(PlaneDefinitions.ControlPlane(host.AddStubs), "work");

        Assert.Equal(ExitCodes.ConfigurationRejected, exitCode);
        Assert.Equal(ErrorCodes.HostingConfigurationRejected.Id, host.Log.Only(7002).Value("Code"));
        Assert.Contains(host.Log.Entries, entry => entry.Message.Contains(ErrorCodes.ConfigNamespaceUnknown.Id, StringComparison.Ordinal));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.HealthPort));
    }

    [Fact]
    public async Task TheControlPlaneAcceptsAnOperatorSection()
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence + Operator);
        using var client = new HttpClient();

        var run = await host.StartAsync(PlaneDefinitions.ControlPlane(host.AddStubs));
        await host.WhenStartedAsync(client);

        Assert.Empty(host.Log.ById(7002));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }
}
