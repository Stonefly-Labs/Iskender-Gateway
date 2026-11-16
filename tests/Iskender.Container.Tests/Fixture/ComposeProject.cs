using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// One run of <c>compose/compose.yaml</c> under a run-scoped project name (research R17): <c>up</c> and <c>down</c>, a
/// service's published health port, its state and exit code from <c>docker inspect</c>, its logs, and waits that poll
/// health within a timeout. The database password is generated per run and passed as a variable, never written to disk.
/// </summary>
internal sealed class ComposeProject : IAsyncDisposable
{
    public const string HealthPort = "8081";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private readonly string _directory;

    private readonly ImageBuild.Images _images;

    private readonly string _databasePassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private ComposeProject(string directory, ImageBuild.Images images, string name)
    {
        _directory = directory;
        _images = images;
        Name = name;
    }

    /// <summary>The run-scoped project name.</summary>
    public string Name { get; }

    /// <summary>The topology under the repository root, for <paramref name="images"/>, with a project name of its own.</summary>
    public static async Task<ComposeProject> CreateAsync(ImageBuild.Images images)
    {
        var root = await ImageBuild.RepositoryRootAsync();
        return new ComposeProject(Path.Combine(root, "tests", "Iskender.Container.Tests", "compose"), images, $"isk-{Guid.NewGuid():N}"[..16]);
    }

    /// <summary>Starts <paramref name="services"/>, or every service, detached. A service that exits is read with <see cref="StateAsync"/>.</summary>
    public Task UpAsync(params string[] services) => RunAsync(["up", "--detach", .. services]);

    /// <summary>Stops and removes the project's containers, networks, and volumes.</summary>
    public Task DownAsync() => RunAsync(["down", "--volumes", "--timeout", "40"]);

    /// <summary>The host port Docker assigned to <paramref name="service"/>'s health listener.</summary>
    public async Task<int> PortAsync(string service)
    {
        var output = await RunAsync(["port", service, HealthPort]);
        return int.Parse(output.Trim().Split('\n')[0].Split(':')[^1], CultureInfo.InvariantCulture);
    }

    /// <summary><paramref name="service"/>'s container state, such as <c>running</c> or <c>exited</c>, and its exit code.</summary>
    public async Task<(string Status, int ExitCode)> StateAsync(string service)
    {
        var id = (await RunAsync(["ps", "--all", "--quiet", service])).Trim();
        var result = await DockerCli.RunAsync(["inspect", "--format", "{{json .State}}", id], CommandTimeout, CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"docker inspect {service} exited {result.ExitCode}: {result.StandardError}");
        using var state = JsonDocument.Parse(result.StandardOutput);
        return (state.RootElement.GetProperty("Status").GetString()!, state.RootElement.GetProperty("ExitCode").GetInt32());
    }

    /// <summary>The services with a running container.</summary>
    public async Task<IReadOnlyList<string>> RunningServicesAsync() =>
        (await RunAsync(["ps", "--services", "--status", "running"])).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary><paramref name="service"/>'s logs, with no prefix.</summary>
    public Task<string> LogsAsync(string service) => RunAsync(["logs", "--no-color", "--no-log-prefix", service]);

    /// <summary>Waits up to <paramref name="timeout"/> for <paramref name="service"/> to exit, and returns its exit code.</summary>
    public async Task<int> WhenExitedAsync(string service, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var (status, exitCode) = await StateAsync(service);
            if (status == "exited")
            {
                return exitCode;
            }

            Assert.True(DateTime.UtcNow < deadline, $"{service} was still {status} after {timeout}.");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>
    /// Polls <paramref name="service"/>'s <paramref name="path"/> every 500 milliseconds until <paramref name="until"/>
    /// accepts the answer, or <paramref name="timeout"/> passes, and returns the last status and body.
    /// </summary>
    public async Task<(HttpStatusCode Status, string Body)> WhenAsync(string service, string path, Func<HttpStatusCode, string, bool> until, TimeSpan timeout)
    {
        var port = await PortAsync(service);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow + timeout;
        (HttpStatusCode Status, string Body) last = (default, "");
        while (true)
        {
            try
            {
                using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}{path}"));
                last = (response.StatusCode, await response.Content.ReadAsStringAsync());
                if (until(last.Status, last.Body))
                {
                    return last;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            Assert.True(DateTime.UtcNow < deadline, $"{service}{path} did not answer as expected within {timeout}; last {(int)last.Status} {last.Body}");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    public async ValueTask DisposeAsync() => await DownAsync();

    private async Task<string> RunAsync(IReadOnlyList<string> arguments)
    {
        var result = await DockerCli.RunAsync(
            ["compose", "--project-name", Name, "--file", Path.Combine(_directory, "compose.yaml"), .. arguments],
            CommandTimeout,
            CancellationToken.None,
            _directory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ISK_COMPOSE_PROJECT"] = Name,
                ["ISK_DATA_PLANE_IMAGE"] = _images.DataPlane,
                ["ISK_CONTROL_PLANE_IMAGE"] = _images.ControlPlane,
                ["ISK_DATABASE_PASSWORD"] = _databasePassword,
            });
        Assert.True(result.ExitCode == 0, $"docker compose {arguments[0]} exited {result.ExitCode}: {result.StandardError}");
        return result.StandardOutput;
    }
}
