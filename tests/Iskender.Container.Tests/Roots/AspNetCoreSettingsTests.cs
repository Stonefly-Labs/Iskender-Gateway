using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Roots;

/// <summary>This collection changes process-wide state, the environment and the content root, so it runs alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessState
{
    public const string Name = "Process state";
}

/// <summary>
/// FR-010 and container contract, Configuration roots rule 6: no ASP.NET Core setting, file, or argument reaches Iskender's
/// configuration, its environment name, or a listener.
/// </summary>
[Collection(ProcessState.Name)]
public sealed class AspNetCoreSettingsTests : IDisposable
{
    private static readonly string[] Variables = ["ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "ASPNETCORE_URLS", "ASPNETCORE_HTTP_PORTS", "DOTNET_URLS"];

    private readonly Dictionary<string, string?> _saved = Variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);

    private readonly string _appSettings = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    private readonly int _otherPort = ContainerTestHost.FreePort();

    public AspNetCoreSettingsTests()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://127.0.0.1:{_otherPort}");
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", _otherPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("DOTNET_URLS", $"http://127.0.0.1:{_otherPort}");
        File.WriteAllText(_appSettings, $$"""
            {
              "urls": "http://127.0.0.1:{{_otherPort}}",
              "Kestrel": { "Endpoints": { "Other": { "Url": "http://127.0.0.1:{{_otherPort}}" } } },
              "iskender": { "container": { "healthPort": {{_otherPort}} }, "persistence": { "tlsMode": "require" } }
            }
            """);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        File.Delete(_appSettings);
    }

    [Fact]
    public async Task TheEnvironmentNameIsStillNoneSoADevelopmentOnlyValueIsRejected()
    {
        using var host = new ContainerTestHost().WithDocument(sections: """
            "persistence":{"host":"db","database":"iskender","username":"isk","password":{"secretRef":"db/runtime"},"tlsMode":"disable"}
            """);

        var exitCode = await host.RunAsync(TestPlanes.WithPersistence(host));

        Assert.Equal(ExitCodes.ConfigurationRejected, exitCode);
        Assert.Single(host.Log.ById(6002));
        Assert.Contains(host.Log.ById(3002), entry => entry.Value("Code") == "config.value.unsafe_for_environment" && entry.Value("Path") == "iskender.persistence.tlsMode");
    }

    [Fact]
    public async Task NoListenerMovesAndNoValueReachesTheSnapshot()
    {
        using var host = new ContainerTestHost().WithDocument();
        var run = await host.StartAsync(TestPlanes.WithTraffic(host));

        Assert.True(await ContainerTestHost.IsListeningAsync(host.HealthPort));
        Assert.True(await ContainerTestHost.IsListeningAsync(host.TrafficPort));
        Assert.False(await ContainerTestHost.IsListeningAsync(_otherPort));

        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }
}
