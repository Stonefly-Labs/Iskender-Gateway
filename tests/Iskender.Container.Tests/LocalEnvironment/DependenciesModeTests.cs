using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// User Story 5, acceptance scenarios 1 to 4, FR-014, and FR-015 (local-environment contract, Modes, Ports, Native runs):
/// the dependencies mode starts only PostgreSQL and Valkey on loopback and builds no plane image; each host's built
/// assembly, run natively with a copy of its native documents, reports the containers' readiness; a port in use is
/// refused naming its variable; and the two modes share one set of volumes.
/// </summary>
[Trait("Category", "Compose")]
public sealed class DependenciesModeTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDependenciesModeServesNativeHostsTheContainersReadiness()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();

        var start = await topology.RunScriptAsync("start.sh", ["--dependencies"]);

        Assert.True(start.ExitCode == 0, $"start.sh --dependencies exited {start.ExitCode}: {start.StandardError}");
        Assert.Equal(["postgres", "valkey"], await topology.RunningServicesAsync());
        Assert.Equal($"127.0.0.1:{topology.PostgresPort}", (await topology.ComposeOutputAsync("port", "postgres", "5432")).Trim());
        Assert.Equal($"127.0.0.1:{topology.ValkeyPort}", (await topology.ComposeOutputAsync("port", "valkey", "6379")).Trim());
        foreach (var plane in new[] { "data-plane", "control-plane" })
        {
            var image = await Fixture.DockerCli.RunAsync(["image", "inspect", $"{topology.Project}/{plane}:local"], TimeSpan.FromMinutes(1), Token);
            Assert.NotEqual(0, image.ExitCode);
        }

        foreach (var (plane, host) in new[] { ("data-plane", "Iskender.Gateway.Host"), ("control-plane", "Iskender.ControlPlane.Host") })
        {
            await using var native = await NativeHost.StartAsync(topology, plane, host, "development");
            var (status, body) = await native.ReadyAsync();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal(RecordedReadiness.Expected, body);
            Assert.Equal(0, await native.StopAsync());
        }
    }

    [Fact]
    public async Task WithNoEnvironmentNameANativeHostReadsNoOverlay()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", ["--dependencies"])).ExitCode);

        await using var native = await NativeHost.StartAsync(topology, "data-plane", "Iskender.Gateway.Host", null, overlay: "iskender: [not: valid: yaml\n");
        var live = await native.LiveAsync();

        Assert.Equal(HttpStatusCode.OK, live);
        Assert.Equal(0, await native.StopAsync());
    }

    [Fact]
    public async Task APostgresPortInUseIsRefusedNamingItsVariable()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        var listener = new TcpListener(IPAddress.Loopback, topology.PostgresPort);
        listener.Start();
        Fixture.DockerResult refused;
        try
        {
            refused = await topology.RunScriptAsync("start.sh", ["--dependencies"]);
        }
        finally
        {
            listener.Stop();
        }

        Assert.Equal(2, refused.ExitCode);
        Assert.Contains("ISKENDER_LOCAL_POSTGRES_PORT", refused.StandardError, StringComparison.Ordinal);
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", ["--dependencies"])).ExitCode);

        // Both ports are now the project's own (contract, Ports rule 3).
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", ["--dependencies"])).ExitCode);
    }

    [Fact]
    public async Task TheModesShareTheVolumesAndTheDependenciesModeStopsThePlanes()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
        var volumes = await VolumesAsync(topology, "postgres");

        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", ["--dependencies"])).ExitCode);

        Assert.Equal(["postgres", "valkey"], await topology.RunningServicesAsync());
        foreach (var plane in new[] { "data-plane", "control-plane" })
        {
            Assert.Equal("exited", (await RestartTests.InspectAsync(topology, plane)).GetProperty("State").GetProperty("Status").GetString());
        }

        Assert.Equal(volumes, await VolumesAsync(topology, "postgres"));
        Assert.Equal(2, (await topology.ProjectResourcesAsync()).Count(resource => resource.StartsWith("volume ", StringComparison.Ordinal)));
    }

    private static async Task<string> VolumesAsync(LocalTopology topology, string service) =>
        string.Join(',', (await RestartTests.InspectAsync(topology, service)).GetProperty("Mounts").EnumerateArray()
            .Where(mount => mount.GetProperty("Type").GetString() == "volume")
            .Select(mount => mount.GetProperty("Name").GetString()));

    /// <summary>
    /// A host's built assembly run with <c>dotnet</c>, from the repository's own build output in the test's configuration,
    /// with a temporary copy of <c>local/native/&lt;plane&gt;/</c> whose ports are the topology's and free listener ports
    /// (local-environment contract, Test fixtures rule 2), and no Iskender variable but the environment name.
    /// </summary>
    private sealed class NativeHost : IAsyncDisposable
    {
        private readonly Process _process;

        private readonly string _directory;

        private NativeHost(Process process, string directory, int healthPort)
        {
            _process = process;
            _directory = directory;
            HealthPort = healthPort;
        }

        public int HealthPort { get; }

        public static async Task<NativeHost> StartAsync(LocalTopology topology, string plane, string host, string? environment, string? overlay = null)
        {
            var root = await Fixture.ImageBuild.RepositoryRootAsync();
            var configuration = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)).Parent!.Name;
            var assembly = Path.Combine(root, "src", host, "bin", configuration, "net10.0", $"{host}.dll");
            Assert.True(File.Exists(assembly), $"{assembly} is missing; build the solution first.");

            var directory = Path.Combine(Path.GetTempPath(), $"iskender-native-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var trafficPort = LocalTopology.FreePort();
            var healthPort = LocalTopology.FreePort();
            var document = (await File.ReadAllTextAsync(Path.Combine(topology.Root, "local", "native", plane, "iskender.yaml")))
                .Replace("    port: 15432", $"    port: {topology.PostgresPort}", StringComparison.Ordinal)
                .Replace("  container:\n    trafficPort: 8090\n    healthPort: 8091\n", "", StringComparison.Ordinal)
                .Replace("iskender:\n", $"iskender:\n  container:\n    trafficPort: {trafficPort}\n    healthPort: {healthPort}\n", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(directory, "iskender.yaml"), document);
            var development = overlay ?? (await File.ReadAllTextAsync(Path.Combine(topology.Root, "local", "native", plane, "iskender.development.yaml")))
                .Replace("127.0.0.1:16379", $"127.0.0.1:{topology.ValkeyPort}", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(directory, "iskender.development.yaml"), development);

            var dotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot && File.Exists(Path.Combine(dotnetRoot, "dotnet"))
                ? Path.Combine(dotnetRoot, "dotnet")
                : "dotnet";
            var start = new ProcessStartInfo(dotnet)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(assembly);
            foreach (var name in start.Environment.Keys.Where(name => name.StartsWith("ISKENDER", StringComparison.Ordinal)).ToList())
            {
                start.Environment.Remove(name);
            }

            start.Environment["ISKENDER_CONFIG_DIRECTORY"] = directory;
            if (environment is not null)
            {
                start.Environment["ISKENDER_ENVIRONMENT"] = environment;
            }

            var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return new NativeHost(process, directory, healthPort);
        }

        /// <summary>The readiness answer, after waiting while lifecycle is <c>starting</c>.</summary>
        public async Task<(HttpStatusCode Status, string Body)> ReadyAsync()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var deadline = DateTime.UtcNow + Settle;
            while (true)
            {
                try
                {
                    using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{HealthPort}/health/ready"), Token);
                    var body = await response.Content.ReadAsStringAsync(Token);
                    if (!Starting(body) || DateTime.UtcNow >= deadline)
                    {
                        return (response.StatusCode, body);
                    }
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline && !_process.HasExited)
                {
                    // Not listening yet.
                }

                Assert.False(_process.HasExited, $"the native host exited {(_process.HasExited ? _process.ExitCode : 0)} before answering.");
                await Task.Delay(TimeSpan.FromMilliseconds(500), Token);
            }
        }

        /// <summary>The liveness status, once the health listener answers.</summary>
        public async Task<HttpStatusCode> LiveAsync()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var deadline = DateTime.UtcNow + Settle;
            while (true)
            {
                try
                {
                    using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{HealthPort}/health/live"), Token);
                    return response.StatusCode;
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline && !_process.HasExited)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), Token);
                }
            }
        }

        /// <summary>Sends SIGTERM and returns the exit code.</summary>
        public async Task<int> StopAsync()
        {
            var signal = await Fixture.DockerCli.RunProcessAsync("kill", ["-TERM", _process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(30), Token);
            Assert.Equal(0, signal.ExitCode);
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(Token);
            bound.CancelAfter(TimeSpan.FromMinutes(1));
            await _process.WaitForExitAsync(bound.Token);
            return _process.ExitCode;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }

            _process.Dispose();
            Directory.Delete(_directory, recursive: true);
        }

        private static bool Starting(string body)
        {
            try
            {
                using var report = JsonDocument.Parse(body);
                return report.RootElement.GetProperty("checks").EnumerateArray().Any(check => check.GetProperty("name").GetString() == "lifecycle" && check.GetProperty("reason").GetString() == "starting");
            }
            catch (JsonException)
            {
                return true;
            }
        }
    }
}
