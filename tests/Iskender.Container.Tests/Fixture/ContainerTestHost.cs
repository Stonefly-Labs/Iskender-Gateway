using System.Net;
using System.Net.Sockets;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Tests;

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
        Environment[Infrastructure.Container.ConfigurationRoots.DirectoryVariable] = ConfigDirectory;
        Files.WithDirectory(ConfigDirectory);
    }

    /// <summary>The configuration directory the environment names.</summary>
    public const string ConfigDirectory = "/config";

    public Dictionary<string, string?> Environment { get; } = new(StringComparer.Ordinal);

    public TestFileSystem Files { get; } = new();

    public ManualTimeProvider Clock { get; } = new();

    public CapturedLog Log { get; } = new();

    public StubPersistenceProbe Persistence { get; } = new();

    public StubCoordinationProbe Coordination { get; } = new();

    public StubSingleInstanceStatus SingleInstance { get; } = new(Domain.Ports.SingleInstanceState.NotApplicable);

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

    /// <summary>Signals a stop, as SIGTERM would.</summary>
    public void Stop() => _stop.Cancel();

    public void Dispose()
    {
        _stop.Dispose();
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
