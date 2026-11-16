using System.Formats.Tar;
using System.Net;
using System.Text.Json;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.Images;

/// <summary>
/// User Story 1, acceptance scenario 7, and SC-007: each image's user and labels, its file list read from the tar of an
/// exported container with each entry's owner and mode, and a hardened run (container contract, Images rule 2).
/// </summary>
[Trait("Category", "Compose")]
public class ImageInspectionTests(ImageBuild build)
{
    private static readonly string[] Shells = ["bin/sh", "bin/bash", "bin/dash", "bin/busybox", "usr/bin/sh", "usr/bin/bash", "usr/bin/dash"];

    private static readonly string[] PackageManagers = ["usr/bin/apt", "usr/bin/apt-get", "usr/bin/dpkg", "usr/bin/rpm", "usr/bin/microdnf", "sbin/apk"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Planes() => new()
    {
        { "data_plane", "Iskender.Management.dll" },
        { "control_plane", "Iskender.Hosting.dll" },
    };

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task TheImageRunsAs1654AndCarriesTheThreeLabels(string plane, string absent)
    {
        _ = absent;
        var image = (await build.GetAsync()).For(plane);

        var result = await Docker(["image", "inspect", "--format", "{{json .Config}}", image]);

        using var config = JsonDocument.Parse(result);
        Assert.Equal("1654:1654", config.RootElement.GetProperty("User").GetString());
        var labels = config.RootElement.GetProperty("Labels");
        Assert.Equal("MIT", labels.GetProperty("org.opencontainers.image.licenses").GetString());
        Assert.Equal("https://github.com/Stonefly-Labs/Iskender-Gateway", labels.GetProperty("org.opencontainers.image.source").GetString());
        Assert.Matches("^[0-9a-f]{40}$", labels.GetProperty("org.opencontainers.image.revision").GetString());
        Assert.False(config.RootElement.TryGetProperty("Healthcheck", out var check) && check.ValueKind != JsonValueKind.Null);
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task TheFileListHoldsThePublishedApplicationOnly(string plane, string absent)
    {
        var image = (await build.GetAsync()).For(plane);
        var entries = await ExportAsync(image);
        var names = entries.Select(entry => entry.Name.TrimStart('.', '/').TrimEnd('/')).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("app/" + (plane == "data_plane" ? "Iskender.Gateway.Host.dll" : "Iskender.ControlPlane.Host.dll"), names);
        Assert.DoesNotContain("app/" + absent, names);
        Assert.DoesNotContain(names, name => name.Contains("/dotnet/sdk/", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.EndsWith(".Tests.dll", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.EndsWith(".cs", StringComparison.Ordinal) || name.EndsWith(".csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(names, Shells.Contains);
        Assert.DoesNotContain(names, PackageManagers.Contains);
        Assert.DoesNotContain(entries, entry => (entry.Mode & (UnixFileMode.SetUser | UnixFileMode.SetGroup)) != 0);
        var application = entries.Where(entry => entry.Name.TrimStart('.', '/').StartsWith("app", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(application);
        Assert.All(application, entry =>
        {
            Assert.Equal(0, entry.Uid);
            Assert.Equal(0, entry.Gid);
            Assert.Equal((UnixFileMode)0, entry.Mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        });
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task AHardenedContainerIsLiveAndStopsWithExitCode0(string plane, string absent)
    {
        _ = absent;
        var image = (await build.GetAsync()).For(plane);
        using var configuration = MountedConfiguration.Unreachable();
        var name = $"isk-image-{Guid.NewGuid():N}"[..24];
        try
        {
            await Docker([
                "run", "--detach", "--name", name, "--read-only", "--tmpfs", "/tmp", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                .. configuration.Mount, "--publish", "127.0.0.1::8081", image]);
            var port = int.Parse((await Docker(["port", name, "8081/tcp"])).Trim().Split('\n')[0].Split(':')[^1], System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(HttpStatusCode.OK, await LivenessAsync(port));

            await Docker(["stop", "--time", "40", name], TimeSpan.FromSeconds(60));
            Assert.Equal("0", (await Docker(["inspect", "--format", "{{.State.ExitCode}}", name])).Trim());
        }
        finally
        {
            await DockerCli.RunAsync(["rm", "--force", name], TimeSpan.FromMinutes(1), CancellationToken.None);
        }
    }

    // Polls once a second for 60 seconds; the process takes a few seconds to start.
    private static async Task<HttpStatusCode> LivenessAsync(int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/health/live", Token);
                return response.StatusCode;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), Token);
            }
        }

        Assert.Fail($"Nothing answered liveness on port {port} within 60 seconds.");
        return default;
    }

    private static async Task<List<TarEntry>> ExportAsync(string image)
    {
        var id = (await Docker(["create", image])).Trim();
        var archive = Path.Combine(Path.GetTempPath(), $"isk-export-{Guid.NewGuid():N}.tar");
        try
        {
            await Docker(["export", "--output", archive, id], TimeSpan.FromMinutes(2));
            var entries = new List<TarEntry>();
            await using var stream = File.OpenRead(archive);
            await using var reader = new TarReader(stream);
            while (await reader.GetNextEntryAsync(copyData: false, Token) is { } entry)
            {
                entries.Add(entry);
            }

            return entries;
        }
        finally
        {
            await DockerCli.RunAsync(["rm", "--force", id], TimeSpan.FromMinutes(1), CancellationToken.None);
            File.Delete(archive);
        }
    }

    private static async Task<string> Docker(IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var result = await DockerCli.RunAsync(arguments, timeout ?? TimeSpan.FromSeconds(60), Token);
        Assert.True(result.ExitCode == 0, $"docker {arguments[0]} exited {result.ExitCode}: {result.StandardError}");
        return result.StandardOutput;
    }
}
