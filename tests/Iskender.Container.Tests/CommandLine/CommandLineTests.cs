using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.CommandLine;

/// <summary>Container contract, Command line and exit codes rule 1: arguments are parsed before any configuration is read.</summary>
public class CommandLineTests
{
    [Theory]
    [InlineData("serve")]
    [InlineData("--urls", "http://*:5000")]
    [InlineData("health")]
    [InlineData("health", "started")]
    [InlineData("health", "live", "extra")]
    [InlineData("HEALTH", "live")]
    [InlineData("migrate", "apply")]
    public async Task AnythingElseIsAUsageErrorBeforeAnyFileIsRead(params string[] args)
    {
        using var host = new ContainerTestHost().WithDocument();

        var exitCode = await host.RunAsync(TestPlanes.ControlPlane(host), args);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains("usage:", host.StandardError.ToString(), StringComparison.Ordinal);
        Assert.Empty(host.Files.Opened);
        Assert.Empty(host.Log.Entries);
    }

    [Fact]
    public async Task ADefinitionCommandReceivesItsArgumentsAndTheResolvedSources()
    {
        using var host = new ContainerTestHost().WithDocument();
        IReadOnlyList<string>? received = null;
        string? baseName = null;
        var definition = TestPlanes.WithCommand(host, "migrate", (args, sources, context, cancellationToken) =>
        {
            received = args;
            baseName = sources.BaseDocument?.SourceName;
            return ValueTask.FromResult(7);
        });

        var exitCode = await host.RunAsync(definition, "migrate", "status");

        Assert.Equal(7, exitCode);
        Assert.Equal(["status"], received);
        Assert.Equal("iskender.json", baseName);
        Assert.Empty(host.Files.Opened);
    }

    [Fact]
    public async Task TheUsageNamesADefinitionsCommands()
    {
        using var host = new ContainerTestHost();

        await host.RunAsync(TestPlanes.WithCommand(host, "migrate", (_, _, _, _) => ValueTask.FromResult(0)), "unknown");

        Assert.Contains("migrate", host.StandardError.ToString(), StringComparison.Ordinal);
    }
}
