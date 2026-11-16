using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Iskender.Domain.Ports;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// Runs a container entry point in process: a test environment dictionary, an in-memory file system, a manual clock, a stop
/// token, a captured log, stub probes, and Kestrel on ports the test chooses (data-model "Test fixtures").
/// </summary>
internal sealed class ContainerTestHost : IDisposable
{
    private readonly CancellationTokenSource _stop = new();

    public ContainerTestHost()
    {
        TrafficPort = FreePort();
        HealthPort = FreePort();
        Environment[ConfigurationRoots.DirectoryVariable] = ConfigDirectory;
        Files.WithDirectory(ConfigDirectory);
    }

    /// <summary>The configuration directory the environment names.</summary>
    public const string ConfigDirectory = "/config";

    public Dictionary<string, string?> Environment { get; } = new(StringComparer.Ordinal);

    public TestFileSystem Files { get; } = new();

    public ManualTimeProvider Clock { get; } = new();

    /// <summary>The clock the entry point uses: the system clock unless a test sets <see cref="Clock"/>.</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>What the entry point wrote to standard error.</summary>
    public StringWriter StandardError { get; } = new();

    public CapturedLog Log { get; } = new();

    public StubPersistenceProbe Persistence { get; } = new();

    public StubCoordinationProbe Coordination { get; } = new();

    public StubSingleInstanceStatus SingleInstance { get; } = new(SingleInstanceState.NotApplicable);

    public int TrafficPort { get; }

    public int HealthPort { get; }

    /// <summary>The token the entry point treats as a stop signal.</summary>
    public CancellationToken StopToken => _stop.Token;

    /// <summary>Writes <c>iskender.json</c> with the chosen ports and <paramref name="sections"/>, the JSON members after <c>container</c>.</summary>
    public ContainerTestHost WithDocument(string containerFields = "", string sections = "")
    {
        var container = $"\"trafficPort\":{TrafficPort},\"healthPort\":{HealthPort}" + (containerFields.Length > 0 ? "," + containerFields : "");
        Files.WithFile($"{ConfigDirectory}/iskender.json", "{\"iskender\":{\"container\":{" + container + "}" + (sections.Length > 0 ? "," + sections : "") + "}}");
        return this;
    }

    /// <summary>Runs the entry point with <paramref name="args"/> and returns its exit code.</summary>
    public Task<int> RunAsync(ContainerPlaneDefinition definition, params string[] args) =>
        ContainerEntryPoint.RunAsync(args, definition, Environment, Time, StopToken, Log, Files, StandardError);

    /// <summary>Starts the entry point serving, and returns once its health listener accepts a connection, or once it has exited.</summary>
    public async Task<Task<int>> StartAsync(ContainerPlaneDefinition definition)
    {
        var run = Task.Run(() => RunAsync(definition));
        await WhenListeningAsync(HealthPort, run);
        return run;
    }

    /// <summary>Registers the stub probes, as the persistence and coordination modules register theirs.</summary>
    public void AddStubs(IServiceCollection services)
    {
        services.AddSingleton<IPersistenceHealthProbe>(Persistence);
        services.AddSingleton<ICoordinationHealthProbe>(Coordination);
        services.AddSingleton<ISingleInstanceStatus>(SingleInstance);
    }

    /// <summary>True when something accepts a connection on <paramref name="port"/> on the loopback interface.</summary>
    public static async Task<bool> IsListeningAsync(int port)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    // Polls until the listener accepts, the run ends, or 30 seconds pass. Each attempt is a connect, not a fixed sleep.
    private static async Task WhenListeningAsync(int port, Task<int> run)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!run.IsCompleted && !await IsListeningAsync(port))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Nothing listened on port {port} within 30 seconds.");
            await Task.Yield();
        }
    }

    /// <summary>Polls until nothing accepts a connection on <paramref name="port"/>, as when the server has stopped accepting, or 30 seconds pass.</summary>
    public static async Task WhenNotListeningAsync(int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await IsListeningAsync(port))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Port {port} still accepted connections after 30 seconds.");
            await Task.Yield();
        }
    }

    /// <summary>One <c>GET /health/ready</c> on the health listener: its status and body.</summary>
    public async Task<(HttpStatusCode Status, JsonElement Body)> ReadyAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"http://127.0.0.1:{HealthPort}/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (response.StatusCode, body.RootElement.Clone());
    }

    /// <summary>Polls readiness until the lifecycle check leaves <c>starting</c>, and returns that answer.</summary>
    public async Task<(HttpStatusCode Status, JsonElement Body)> WhenStartedAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var answer = await ReadyAsync(client);
            if (Reason(answer.Body, "lifecycle") != "starting")
            {
                return answer;
            }

            Assert.True(DateTime.UtcNow < deadline, "The lifecycle check stayed starting for 30 seconds.");
            await Task.Yield();
        }
    }

    /// <summary>The reason of check <paramref name="name"/> in a readiness body.</summary>
    public static string? Reason(JsonElement body, string name) => Check(body, name).GetProperty("reason").GetString();

    /// <summary>The status of check <paramref name="name"/> in a readiness body: <c>ready</c> or <c>not_ready</c>.</summary>
    public static string? Status(JsonElement body, string name) => Check(body, name).GetProperty("status").GetString();

    private static JsonElement Check(JsonElement body, string name) =>
        body.GetProperty("checks").EnumerateArray().Single(check => check.GetProperty("name").GetString() == name);

    /// <summary>Signals a stop, as SIGTERM would.</summary>
    public void Stop() => _stop.Cancel();

    public void Dispose()
    {
        _stop.Dispose();
        StandardError.Dispose();
        Log.Dispose();
    }

    /// <summary>A port no listener holds now. Another process could take it before the test binds it; tests that need a held port bind one themselves.</summary>
    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
