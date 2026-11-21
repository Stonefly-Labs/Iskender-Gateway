using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.Lifecycle;

/// <summary>
/// The worker on the control-plane image (ISK-0009 T051, research R11, container contract Command line rule 1 and
/// Lifecycle, US6.1, US6.3, US2.5, SC-007): <c>work</c> answers liveness and readiness with the five checks, its events
/// name plane <c>worker</c>, and on <c>SIGTERM</c> its readiness turns <c>stopping</c>, 7004 then 7005 are written, and it
/// exits 0 within <c>shutdownDelay</c> + <c>shutdownTimeout</c> + 5 seconds. <c>work</c> on the data plane and
/// <c>work extra</c> exit 64, a <c>work</c> section in either serving plane's document exits 2, as does a worker whose
/// <c>work</c> section fails a cross-field check, and a worker whose leases are Redis's exits 4 naming <c>work.dispatch</c>. The database never resolves, as in the other image tests.
/// </summary>
[Trait("Category", "Compose")]
public class WorkerCommandTests(ImageBuild build)
{
    private static readonly TimeSpan ShutdownDelay = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] Checks = ["lifecycle", "persistence", "coordination", "single_instance", "capability"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WorkServesTheFiveChecksAndStopsWithinItsBoundOnSigterm()
    {
        var image = (await build.GetAsync()).ControlPlane;
        using var configuration = new MountedConfiguration(Document(
            "\"coordination\":{\"leases\":{\"coordinator\":\"postgresql\"}},"
            + $"\"container\":{{\"shutdownDelay\":\"{ShutdownDelay.TotalSeconds}s\",\"shutdownTimeout\":\"{ShutdownTimeout.TotalSeconds}s\",\"readinessCacheDuration\":\"0s\"}}"));
        var name = $"isk-worker-{Guid.NewGuid():N}"[..24];
        try
        {
            await DockerAsync(["run", "--detach", "--name", name, "--read-only", "--tmpfs", "/tmp", .. configuration.Mount, "--publish", "127.0.0.1::8081", image, "work"]);
            var port = int.Parse((await DockerAsync(["port", name, "8081/tcp"])).Trim().Split('\n')[0].Split(':')[^1], CultureInfo.InvariantCulture);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            Assert.Equal(HttpStatusCode.OK, await WhenAnswersAsync(client, port, "live"));

            var (status, body) = await ReadyAsync(client, port);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal(1, body.GetProperty("version").GetInt32());
            Assert.Equal(Checks, body.GetProperty("checks").EnumerateArray().Select(check => check.GetProperty("name").GetString()));

            var stopping = Stopwatch.StartNew();
            await DockerAsync(["kill", "--signal", "SIGTERM", name]);
            var (_, signalled) = await ReadyAsync(client, port);
            var exitCode = (await DockerAsync(["wait", name], TimeSpan.FromMinutes(1))).Trim();
            stopping.Stop();

            Assert.Equal("stopping", Reason(signalled, "lifecycle"));
            Assert.Equal("0", exitCode);
            Assert.InRange(stopping.Elapsed, TimeSpan.Zero, ShutdownDelay + ShutdownTimeout + TimeSpan.FromSeconds(5));
            var events = await EventsAsync(name);
            Assert.Equal([7001, 7004, 7005], events.Where(logged => logged.Id is 7001 or 7004 or 7005).Select(logged => logged.Id));
            Assert.All(events.Where(logged => logged.Id is 7001 or 7004 or 7005), logged => Assert.Equal("worker", logged.State["Plane"]!.GetValue<string>()));
        }
        finally
        {
            await DockerCli.RunAsync(["rm", "--force", name], TimeSpan.FromMinutes(1), CancellationToken.None);
        }
    }

    [Fact]
    public async Task WorkOnTheDataPlaneAndWorkExtraExit64()
    {
        var images = await build.GetAsync();

        Assert.Equal(64, (await RunAsync(images.DataPlane, [], ["work"])).ExitCode);
        Assert.Equal(64, (await RunAsync(images.ControlPlane, [], ["work", "extra"])).ExitCode);
    }

    [Theory]
    [InlineData("data_plane")]
    [InlineData("control_plane")]
    public async Task AWorkSectionInAServingPlanesDocumentExits2(string plane)
    {
        var image = (await build.GetAsync()).For(plane);
        using var configuration = new MountedConfiguration(Document("\"work\":{}"));

        var result = await RunAsync(image, configuration.Mount, []);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("config.namespace.unknown", result.StandardOutput, StringComparison.Ordinal);
    }

    // dispatch.shards 3 is within the schema's 1 to 64 but does not divide 64, which the worker's own check refuses at
    // start (ISK-0009 T101, container contract Test fixtures rule 5, FR-029).
    [Fact]
    public async Task AWorkerWhoseWorkSectionFailsACrossFieldCheckExits2()
    {
        var image = (await build.GetAsync()).ControlPlane;
        using var configuration = new MountedConfiguration(Document(
            "\"coordination\":{\"leases\":{\"coordinator\":\"postgresql\"}},\"work\":{\"dispatch\":{\"shards\":3}}"));

        var result = await RunAsync(image, configuration.Mount, ["work"]);

        Assert.Equal(2, result.ExitCode);
        var refused = Events(result.StandardOutput).Single(logged => logged.Id == 7002);
        Assert.Equal("config.field.out_of_range", refused.State["Code"]!.GetValue<string>());
    }

    [Fact]
    public async Task AWorkerWhoseLeasesAreRedissExits4NamingWorkDispatch()
    {
        var image = (await build.GetAsync()).ControlPlane;
        using var configuration = new MountedConfiguration(Document(
            "\"coordination\":{\"leases\":{\"coordinator\":\"redis\"},"
            + "\"redis\":{\"endpoints\":[\"cache-1.invalid:6379\"],\"username\":\"isk_coordination\",\"password\":{\"secretRef\":\"redis/coordination\"}}}"));

        var result = await RunAsync(image, configuration.Mount, ["work"]);

        Assert.Equal(4, result.ExitCode);
        var refused = Events(result.StandardOutput).Single(logged => logged.Id == 7002);
        Assert.Equal(("work.dispatch", "4"), (refused.State["Feature"]!.GetValue<string>(), refused.State["ExitCode"]!.ToString()));
    }

    private static string Document(string sections) => "{\"iskender\":{" + PlaneDefinitions.Persistence + "," + sections + "}}";

    private static string? Reason(JsonElement body, string check) =>
        body.GetProperty("checks").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == check).GetProperty("reason").GetString();

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadyAsync(HttpClient client, int port)
    {
        using var response = await client.GetAsync($"http://127.0.0.1:{port}/health/ready", Token);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone());
    }

    // Polls once a second for 60 seconds; the process takes a few seconds to start.
    private static async Task<HttpStatusCode> WhenAnswersAsync(HttpClient client, int port, string check)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/health/{check}", Token);
                return response.StatusCode;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), Token);
            }
        }

        Assert.Fail($"Nothing answered {check} on port {port} within 60 seconds.");
        return default;
    }

    private static async Task<IReadOnlyList<(int Id, JsonObject State)>> EventsAsync(string name) =>
        Events((await DockerCli.RunAsync(["logs", name], TimeSpan.FromMinutes(1), Token)).StandardOutput);

    // Each JSON line's event ID and state, in the order written.
    private static List<(int Id, JsonObject State)> Events(string output) =>
        [.. output.Split('\n')
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(entry => entry["EventId"] is not null && entry["State"] is JsonObject)
            .Select(entry => (entry["EventId"]!.GetValue<int>(), entry["State"]!.AsObject()))];

    private static Task<DockerResult> RunAsync(string image, IReadOnlyList<string> options, IReadOnlyList<string> command) =>
        DockerCli.RunAsync(["run", "--rm", "--read-only", "--tmpfs", "/tmp", .. options, image, .. command], TimeSpan.FromMinutes(2), Token);

    private static async Task<string> DockerAsync(IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var result = await DockerCli.RunAsync(arguments, timeout ?? TimeSpan.FromMinutes(2), Token);
        Assert.True(result.ExitCode == 0, $"docker {arguments[0]} exited {result.ExitCode}: {result.StandardError}");
        return result.StandardOutput;
    }
}
