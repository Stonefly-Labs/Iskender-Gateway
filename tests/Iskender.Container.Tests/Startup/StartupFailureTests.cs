using System.Net;
using System.Net.Sockets;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Startup;

/// <summary>Container contract, Start-up rule 1, and Command line and exit codes rule 2: codes 1, 2, and 3.</summary>
public class StartupFailureTests
{
    private const string Canary = "canary-value-7c41";

    private static void AssertRefused(ContainerTestHost host, int exitCode, string code)
    {
        var refused = host.Log.Only(7002);
        Assert.Equal(code, refused.Value("Code"));
        Assert.Equal(exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture), refused.Value("ExitCode"));
        var stopped = host.Log.Only(7005);
        Assert.Equal(exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture), stopped.Value("ExitCode"));
        Assert.True(host.Log.Entries.ToList().IndexOf(refused) < host.Log.Entries.ToList().IndexOf(stopped));
        Assert.Single(host.Log.ById(7001));
    }

    [Fact]
    public async Task ARejectedConfigurationExits2WithItsEventsAndNoValue()
    {
        using var host = new ContainerTestHost().WithDocument(sections: $"\"unknownSection\":{{\"secret\":\"{Canary}\"}}");

        var exitCode = await host.RunAsync(TestPlanes.ControlPlane(host));

        Assert.Equal(ExitCodes.ConfigurationRejected, exitCode);
        Assert.NotEmpty(host.Log.ById(3002));
        Assert.Single(host.Log.ById(3003));
        Assert.Single(host.Log.ById(6002));
        AssertRefused(host, 2, "hosting.configuration_rejected");
        Assert.All(host.Log.Entries, entry => Assert.DoesNotContain(Canary, entry.Message, StringComparison.Ordinal));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.HealthPort));
    }

    [Fact]
    public async Task AMissingNamedDirectoryExits2()
    {
        using var host = new ContainerTestHost();
        host.Environment[ConfigurationRoots.DirectoryVariable] = "/missing";

        Assert.Equal(ExitCodes.ConfigurationRejected, await host.RunAsync(TestPlanes.ControlPlane(host)));
        Assert.Single(host.Log.ById(6002));
        AssertRefused(host, 2, "hosting.configuration_rejected");
    }

    [Fact]
    public async Task AnAmbiguousLayerExits2()
    {
        using var host = new ContainerTestHost().WithDocument();
        host.Files.WithFile($"{ContainerTestHost.ConfigDirectory}/iskender.yaml", "iskender: {}");

        Assert.Equal(ExitCodes.ConfigurationRejected, await host.RunAsync(TestPlanes.ControlPlane(host)));
        Assert.Single(host.Log.ById(6002));
        AssertRefused(host, 2, "hosting.configuration_rejected");
        Assert.Empty(host.Files.Opened);
    }

    [Fact]
    public async Task AnUnreadableLayerExits2()
    {
        using var host = new ContainerTestHost();
        host.Files.WithUnreadableFile($"{ContainerTestHost.ConfigDirectory}/iskender.json");

        Assert.Equal(ExitCodes.ConfigurationRejected, await host.RunAsync(TestPlanes.ControlPlane(host)));
        Assert.Single(host.Log.ById(6002));
        AssertRefused(host, 2, "hosting.configuration_rejected");
    }

    [Fact]
    public async Task EqualPortsExit2BeforeAnyPortIsBound()
    {
        using var host = new ContainerTestHost();
        host.Files.WithFile($"{ContainerTestHost.ConfigDirectory}/iskender.json", $"{{\"iskender\":{{\"container\":{{\"trafficPort\":{host.HealthPort},\"healthPort\":{host.HealthPort}}}}}}}");

        Assert.Equal(ExitCodes.ConfigurationRejected, await host.RunAsync(TestPlanes.WithTraffic(host)));
        AssertRefused(host, 2, "config.field.out_of_range");
        Assert.DoesNotContain(host.Log.Entries, entry => entry.Category.StartsWith("Microsoft.AspNetCore.Server.Kestrel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADefinitionThatOmitsARequiredModuleExits3()
    {
        using var host = new ContainerTestHost().WithDocument();
        var definition = TestPlanes.Only((services, _) => services.AddIskenderContainer());

        Assert.Equal(ExitCodes.RegistrationMissing, await host.RunAsync(definition));
        Assert.Single(host.Log.ById(6003));
        AssertRefused(host, 3, "hosting.registration_missing");
    }

    [Fact]
    public async Task APortAlreadyInUseExits1NamingOnlyTheExceptionType()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var held = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true };
        held.Bind(new IPEndPoint(IPAddress.IPv6Any, host.HealthPort));
        held.Listen();

        var exitCode = await host.RunAsync(TestPlanes.ControlPlane(host));

        Assert.Equal(ExitCodes.Unexpected, exitCode);
        var stopped = host.Log.Only(7005);
        Assert.Equal("1", stopped.Value("ExitCode"));
        Assert.Equal("IOException", stopped.Value("Failure"));
        Assert.Empty(host.Log.ById(7002));
    }
}
