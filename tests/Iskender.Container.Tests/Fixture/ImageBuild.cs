using Iskender.Container.Tests.Fixture;

[assembly: AssemblyFixture(typeof(ImageBuild))]

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// Builds both images once per test run, the first time a test asks for them, with <c>docker build</c> from the
/// repository root, tagged with this run's ID and labelled with <c>HEAD</c>, and removes them when the run ends (research
/// R17). A test with no container runtime is skipped, or failed when <c>CI</c> is <c>true</c>; a build that fails fails
/// every test that needs the images, and is never a skip.
/// </summary>
public sealed class ImageBuild : IAsyncDisposable
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);

    private readonly string _runId = Guid.NewGuid().ToString("N")[..12];

    private readonly SemaphoreSlim _gate = new(1, 1);

    private Images? _images;

    private string? _failure;

    /// <summary>The image tags, built on first use. Skips without a container runtime, as <see cref="RequireDocker"/> decides.</summary>
    public async Task<Images> GetAsync()
    {
        await RequireDocker.ThatAsync();
        await _gate.WaitAsync();
        try
        {
            if (_failure is null && _images is null)
            {
                try
                {
                    _images = await BuildAsync();
                }
                catch (Exception exception) when (exception is TimeoutException or InvalidOperationException)
                {
                    _failure = exception.Message;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        if (_failure is not null)
        {
            Assert.Fail($"The images could not be built: {_failure}");
        }

        return _images!;
    }

    public async ValueTask DisposeAsync()
    {
        if (_images is { } images)
        {
            await DockerCli.RunAsync(["image", "rm", "--force", images.DataPlane, images.ControlPlane], TimeSpan.FromMinutes(1), CancellationToken.None);
        }

        _gate.Dispose();
    }

    /// <summary>The repository root, from <c>git rev-parse --show-toplevel</c>.</summary>
    public static async Task<string> RepositoryRootAsync()
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --show-toplevel")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        using var git = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var root = (await git.StandardOutput.ReadToEndAsync()).Trim();
        await git.WaitForExitAsync();
        return git.ExitCode == 0 && root.Length > 0 ? root : throw new InvalidOperationException("git rev-parse --show-toplevel failed.");
    }

    private async Task<Images> BuildAsync()
    {
        var root = await RepositoryRootAsync();
        var revision = await RevisionAsync(root);
        var images = new Images($"iskender-data-plane:test-{_runId}", $"iskender-control-plane:test-{_runId}");
        await BuildAsync(root, "src/Iskender.Gateway.Host/Dockerfile", images.DataPlane, revision);
        await BuildAsync(root, "src/Iskender.ControlPlane.Host/Dockerfile", images.ControlPlane, revision);
        return images;
    }

    private static async Task BuildAsync(string root, string dockerfile, string tag, string revision)
    {
        var result = await DockerCli.RunAsync(["build", "-f", dockerfile, "-t", tag, "--build-arg", $"REVISION={revision}", "."], BuildTimeout, CancellationToken.None, root);
        if (result.ExitCode != 0)
        {
            var tail = string.Join('\n', result.StandardError.Split('\n').TakeLast(20));
            throw new InvalidOperationException($"docker build of {tag} exited {result.ExitCode}:\n{tail}");
        }
    }

    private static async Task<string> RevisionAsync(string root)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = root };
        using var git = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var revision = (await git.StandardOutput.ReadToEndAsync()).Trim();
        await git.WaitForExitAsync();
        return revision;
    }

    /// <summary>The two tags this run built.</summary>
    public sealed record Images(string DataPlane, string ControlPlane)
    {
        public string For(string plane) => plane == "data_plane" ? DataPlane : ControlPlane;
    }
}
