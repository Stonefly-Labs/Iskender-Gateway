using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.CommandLine;

/// <summary>The <c>health live</c> and <c>health ready</c> commands (container contract, Command line and exit codes rule 1; research R13).</summary>
public class HealthCommandTests
{
    [Fact]
    public async Task LiveExits0OnALiveListenerAndWritesNoEvent()
    {
        using var server = new ContainerTestHost().WithDocument();
        var run = await server.StartAsync(TestPlanes.ControlPlane(server));
        using var check = new ContainerTestHost();
        check.Files.WithFile($"{ContainerTestHost.ConfigDirectory}/iskender.json", ReadDocument(server));

        var exitCode = await check.RunAsync(TestPlanes.ControlPlane(check), "health", "live");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(check.Log.Entries);
        server.Stop();
        await run;
    }

    [Theory]
    [InlineData("live")]
    [InlineData("ready")]
    public async Task ACheckWithNoListenerExits1(string name)
    {
        using var host = new ContainerTestHost().WithDocument();

        Assert.Equal(ExitCodes.Unexpected, await host.RunAsync(TestPlanes.ControlPlane(host), "health", name));
        Assert.Empty(host.Log.Entries);
    }

    // Every adapter the entry points register resolves in the check's validated container (tasks note "Container validation").
    [Theory]
    [InlineData("data_plane")]
    [InlineData("control_plane")]
    public async Task EachPlaneWithEveryAdapterChecksWithNoListenerAndWritesNoEvent(string plane)
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        var definition = plane == "data_plane" ? PlaneDefinitions.DataPlane() : PlaneDefinitions.ControlPlane();

        Assert.Equal(ExitCodes.Unexpected, await host.RunAsync(definition, "health", "live"));
        Assert.Empty(host.Log.Entries);
        Assert.Empty(host.StandardError.ToString());
    }

    // The check cannot tell which command the container serves, so a worker's document, with its work section, checks as
    // the plane's does, and the section is still validated: a field it does not declare is rejected (ISK-0009 T064,
    // research R11, FR-025, FR-029).
    [Theory]
    [InlineData("{}", ExitCodes.Success)]
    [InlineData("{\"batchSize\":0}", ExitCodes.Unexpected)]
    [InlineData("{\"unknownField\":1}", ExitCodes.Unexpected)]
    public async Task AWorkersDocumentWithAWorkSectionChecksAgainstTheListener(string work, int expected)
    {
        using var server = new ContainerTestHost().WithDocument();
        var run = await server.StartAsync(TestPlanes.ControlPlane(server));
        using var check = new ContainerTestHost();
        var document = ReadDocument(server);
        check.Files.WithFile(
            $"{ContainerTestHost.ConfigDirectory}/iskender.json",
            string.Concat(document.AsSpan(0, document.Length - 2), $",{PlaneDefinitions.Persistence},\"work\":{work}}}}}"));

        var exitCode = await check.RunAsync(PlaneDefinitions.ControlPlane(), "health", "live");

        Assert.Equal(expected, exitCode);
        Assert.Empty(check.Log.Entries);
        server.Stop();
        await run;
    }

    [Fact]
    public async Task ARejectedConfigurationExits1AndWritesNoEvent()
    {
        using var host = new ContainerTestHost().WithDocument(sections: "\"unknownSection\":{}");

        Assert.Equal(ExitCodes.Unexpected, await host.RunAsync(TestPlanes.ControlPlane(host), "health", "live"));
        Assert.Empty(host.Log.Entries);
    }

    [Fact]
    public async Task AMissingNamedDirectoryExits1()
    {
        using var host = new ContainerTestHost();
        host.Environment[ConfigurationRoots.DirectoryVariable] = "/missing";

        Assert.Equal(ExitCodes.Unexpected, await host.RunAsync(TestPlanes.ControlPlane(host), "health", "live"));
    }

    private static string ReadDocument(ContainerTestHost host)
    {
        using var reader = new StreamReader(host.Files.OpenRead($"{ContainerTestHost.ConfigDirectory}/iskender.json"));
        return reader.ReadToEnd();
    }
}
