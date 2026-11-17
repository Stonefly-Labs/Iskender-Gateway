using System.Buffers.Binary;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Iskender.Domain.Ports;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// User Story 1, acceptance scenario 2, and the edge case with no credential provider: a host composes PostgreSQL
/// persistence in process and opens no connection at start. No container is needed (research R17).
/// </summary>
public class EmbeddedPostgreSqlCompositionTests
{
    // A host name under the reserved .invalid domain, which never resolves.
    private const string Document = """
        {"iskender":{"persistence":{"host":"iskender-database.invalid","database":"iskender","username":"gateway","password":{"secretRef":"database/runtime"}}}}
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AHostWithPostgreSqlPersistenceStartsAndOpensNoConnection()
    {
        var credentials = new RecordingCredentials();

        await using var host = await EmbeddedHost.CreateAsync(builder => Compose(builder, credentials), ct: Token);
        var units = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        using var gauges = new HeldConnectionGauges(host.Services.GetRequiredService<PostgreSqlDataSources>());

        Assert.IsType<PostgreSqlUnitOfWorkFactory>(units);
        Assert.Equal("hosting,persistence-postgresql", host.Logs.Only(6001).Value("Modules"));
        Assert.Equal(1, gauges.Published);
        Assert.Equal(0, gauges.Held());
        Assert.Equal(0, credentials.Calls);
    }

    /// <summary>
    /// The driver asks for a password only once a server requests one, so the probe is pointed at a loopback listener in
    /// this test that requests a cleartext password, and the missing provider shows as <c>authentication_failed</c>.
    /// </summary>
    [Fact]
    public async Task WithNoCredentialProviderStartSucceedsAndTheHealthProbeReportsAuthenticationFailed()
    {
        using var server = new PasswordRequestingServer();
        var document = "{\"iskender\":{\"persistence\":{\"host\":\"127.0.0.1\",\"port\":" + server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"database\":\"iskender\",\"username\":\"gateway\",\"password\":{\"secretRef\":\"database/runtime\"},\"tlsMode\":\"disable\"}}}";
        await using var host = await EmbeddedHost.CreateAsync(builder => Compose(builder, credentials: null, document, "development"), ct: Token);

        var health = await host.Services.GetRequiredService<IPersistenceHealthProbe>().CheckAsync(Token);

        Assert.Equal(PersistenceHealthStatus.Unhealthy, health.Status);
        Assert.Equal(PersistenceHealthReason.AuthenticationFailed, health.Reason);
    }

    private static void Compose(WebApplicationBuilder builder, IDatabaseCredentialProvider? credentials, string document = Document, string? environmentName = null)
    {
        builder.Services.AddIskender(options =>
        {
            options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document);
            options.EnvironmentName = environmentName;
        });
        builder.Services.AddIskenderPostgreSqlPersistence();
        if (credentials is not null)
        {
            builder.Services.AddSingleton(credentials);
        }
    }

    private sealed class RecordingCredentials : IDatabaseCredentialProvider
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<DatabaseCredential> GetCredentialAsync(DatabaseCredentialRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("No credential is read at start.");
        }
    }

    // The held-connection gauge of one host's data sources, told from other hosts' gauges by its meter's scope, since
    // other hosting tests in this assembly open real database connections in parallel. Starting the listener publishes
    // the gauge, which already exists.
    private sealed class HeldConnectionGauges : IDisposable
    {
        private readonly MeterListener _listener = new();
        private int _published;
        private long _held;

        public HeldConnectionGauges(PostgreSqlDataSources sources)
        {
            _listener.InstrumentPublished = (instrument, listening) =>
            {
                if (instrument.Name == "iskender.persistence.pool.connections" && ReferenceEquals(instrument.Meter.Scope, sources))
                {
                    Interlocked.Increment(ref _published);
                    listening.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref _held, value));
            _listener.Start();
        }

        public int Published => Volatile.Read(ref _published);

        public long Held()
        {
            Interlocked.Exchange(ref _held, 0);
            _listener.RecordObservableInstruments();
            return Interlocked.Read(ref _held);
        }

        public void Dispose() => _listener.Dispose();
    }

    // Answers each connection's startup message with a cleartext password request and nothing more: an SSL request is
    // declined, and whatever the client sends after the request is ignored until it closes.
    private sealed class PasswordRequestingServer : IDisposable
    {
        private static readonly byte[] CleartextPasswordRequest = [(byte)'R', 0, 0, 0, 8, 0, 0, 0, 3];

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();

        public PasswordRequestingServer()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            _stopping.Dispose();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    _ = AnswerAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
            catch (SocketException)
            {
                // Stopped.
            }
        }

        private async Task AnswerAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    while (await ReadStartupCodeAsync(stream) == 80877103)
                    {
                        await stream.WriteAsync("N"u8.ToArray(), _stopping.Token);
                    }

                    await stream.WriteAsync(CleartextPasswordRequest, _stopping.Token);
                    var buffer = new byte[256];
                    while (await stream.ReadAsync(buffer, _stopping.Token) > 0)
                    {
                    }
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    // The client went away, or the server stopped.
                }
            }
        }

        // A startup packet is a big-endian length, including itself, then a 4-byte code: 80877103 asks for SSL.
        private async Task<int> ReadStartupCodeAsync(NetworkStream stream)
        {
            var header = new byte[8];
            await stream.ReadExactlyAsync(header, _stopping.Token);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length > 8)
            {
                await stream.ReadExactlyAsync(new byte[length - 8], _stopping.Token);
            }

            return BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
        }
    }
}
