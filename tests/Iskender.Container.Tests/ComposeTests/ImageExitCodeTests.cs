using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.ComposeTests;

/// <summary>
/// Container contract, Command line and exit codes, run on the images: usage is 64, an unusable configuration root, an
/// invalid environment name, and the host's environment relaxing a default are each 2, and <c>health ready</c> is 1 when the
/// process is not ready or no health listener answers.
/// </summary>
[Trait("Category", "Compose")]
public class ImageExitCodeTests(ImageBuild build)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Planes() => ["data_plane", "control_plane"];

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task AnUnknownCommandAndAHealthCommandWithNoCheckExit64(string plane)
    {
        var image = (await build.GetAsync()).For(plane);

        Assert.Equal(64, await RunAsync(image, [], ["nonsense"]));
        Assert.Equal(64, await RunAsync(image, [], ["health"]));
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task AnUnusableRootOrAnUnsafeEnvironmentExits2(string plane)
    {
        var image = (await build.GetAsync()).For(plane);
        using var configuration = MountedConfiguration.Unreachable();
        using var unsafeDocument = new MountedConfiguration("{\"iskender\":{" + PlaneDefinitions.Container + "," + PlaneDefinitions.Persistence[..^1] + ",\"tlsMode\":\"disable\"}}}");

        Assert.Equal(2, await RunAsync(image, ["--env", "ISKENDER_CONFIG_DIRECTORY=/nowhere"], []));
        Assert.Equal(2, await RunAsync(image, ["--env", "ISKENDER_ENVIRONMENT=Prod_EU", .. configuration.Mount], []));
        Assert.Equal(2, await RunAsync(image, ["--env", "ASPNETCORE_ENVIRONMENT=Development", .. unsafeDocument.Mount], []));
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task HealthReadyExits1WhenNotReadyAndWhenNoListenerAnswers(string plane)
    {
        var image = (await build.GetAsync()).For(plane);
        var host = plane == "data_plane" ? "Iskender.Gateway.Host" : "Iskender.ControlPlane.Host";
        using var configuration = MountedConfiguration.Unreachable();
        var name = $"isk-exit-{Guid.NewGuid():N}"[..24];
        try
        {
            await DockerAsync(["run", "--detach", "--name", name, "--read-only", "--tmpfs", "/tmp", .. configuration.Mount, image]);
            await WhenLiveAsync(name, host);

            // Persistence is unreachable, so the running process is not ready.
            var notReady = await DockerCli.RunAsync(["exec", name, "dotnet", $"/app/{host}.dll", "health", "ready"], TimeSpan.FromMinutes(1), Token);
            Assert.Equal(1, notReady.ExitCode);
        }
        finally
        {
            await DockerCli.RunAsync(["rm", "--force", name], TimeSpan.FromMinutes(1), CancellationToken.None);
        }

        // A container whose command is the check itself has no server, so nothing listens on the health port.
        Assert.Equal(1, await RunAsync(image, configuration.Mount, ["health", "ready"]));
    }

    private static async Task<int> RunAsync(string image, IReadOnlyList<string> options, IReadOnlyList<string> command)
    {
        var result = await DockerCli.RunAsync(["run", "--rm", "--read-only", "--tmpfs", "/tmp", .. options, image, .. command], TimeSpan.FromMinutes(2), Token);
        return result.ExitCode;
    }

    // health live, run in the container, exits 0 once its listener answers.
    private static async Task WhenLiveAsync(string name, string host)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var live = await DockerCli.RunAsync(["exec", name, "dotnet", $"/app/{host}.dll", "health", "live"], TimeSpan.FromMinutes(1), Token);
            if (live.ExitCode == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), Token);
        }

        Assert.Fail($"{name} was not live within 60 seconds.");
    }

    private static async Task DockerAsync(IReadOnlyList<string> arguments)
    {
        var result = await DockerCli.RunAsync(arguments, TimeSpan.FromMinutes(2), Token);
        Assert.True(result.ExitCode == 0, $"docker {arguments[0]} exited {result.ExitCode}: {result.StandardError}");
    }
}
