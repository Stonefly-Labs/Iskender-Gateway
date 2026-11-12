using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis.Tests.Connections;

public partial class RedisConnectionTests(RedisFixture fixture)
{
    private static readonly CoordinationName Counter = new("fixture", "connection_counter");

    private static readonly string[] TwoEndpoints = ["cache-1.internal:6390", "[::1]:6391"];

    [Fact]
    public async Task TheProviderIsCalledOnceWhenTheMultiplexerIsBuilt()
    {
        fixture.RequireRedis();
        var provider = TestCoordinationCredentialProvider.For(fixture);
        await using var connection = new RedisConnection(TestCoordinationSettings.For(fixture), provider);

        await Read(connection);
        await Read(connection);
        await Read(connection);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, connection.Builds);
        Assert.Equal(TestCoordinationSettings.PasswordSecret.Name, Assert.Single(provider.Requests).Reference.Name);
    }

    [Fact]
    public async Task AfterAWrongPasswordTheProviderIsCalledAgainOnlyOncePerConnectTimeout()
    {
        fixture.RequireRedis();
        var (username, password) = await DedicatedUserAsync();
        var provider = new TestCoordinationCredentialProvider(new Dictionary<string, string>(StringComparer.Ordinal) { [TestCoordinationSettings.PasswordSecret.Name] = password })
        {
            Override = "not-the-password",
        };
        var time = new ShiftedTimeProvider();
        var settings = TestCoordinationSettings.For(fixture, new Dictionary<string, object> { ["username"] = username, ["connectTimeout"] = "1s" });
        await using var connection = new RedisConnection(settings, provider, timeProvider: time);

        var refused = await Assert.ThrowsAsync<GatewayException>(() => Read(connection));
        Assert.Equal(CoordinationFailureClass.AuthenticationFailed, CoordinationFailures.ClassOf(refused));
        Assert.Equal(1, provider.Calls);

        // Within connectTimeout of the last build, the provider is not asked again.
        provider.Override = null;
        var throttled = await Assert.ThrowsAsync<GatewayException>(() => Read(connection));
        Assert.Equal(CoordinationFailureClass.AuthenticationFailed, CoordinationFailures.ClassOf(throttled));
        Assert.Equal(1, provider.Calls);

        time.Advance(settings.RedisConnectTimeout);
        await Read(connection);

        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, connection.Builds);
    }

    [Fact]
    public async Task AMissingProviderFailsWithAuthenticationFailed()
    {
        fixture.RequireRedis();
        await using var connection = new RedisConnection(TestCoordinationSettings.For(fixture), credentials: null);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => Read(connection));

        Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
        Assert.Equal(CoordinationFailureClass.AuthenticationFailed, CoordinationFailures.ClassOf(failure));
        Assert.Equal(0, connection.Builds);
    }

    [Fact]
    public async Task AThrowingProviderFailsWithAuthenticationFailedAndItsMessageAppearsNowhere()
    {
        fixture.RequireRedis();
        var planted = "planted-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var provider = TestCoordinationCredentialProvider.For(fixture);
        provider.Failure = new InvalidOperationException(planted);
        var logger = new CapturingDiagnostics.Logger();
        await using var connection = new RedisConnection(TestCoordinationSettings.For(fixture), provider, logger);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => Read(connection));

        Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
        Assert.Equal(CoordinationFailureClass.AuthenticationFailed, CoordinationFailures.ClassOf(failure));
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(planted, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(failure.Error!.Diagnostics.Values, pair => pair.Value.Contains(planted, StringComparison.Ordinal));
        Assert.All(logger.Events, log =>
        {
            Assert.DoesNotContain(planted, log.Message, StringComparison.Ordinal);
            Assert.Null(log.Exception);
        });
        Assert.Contains(logger.Events, log => log.Id.Id == 5003);
    }

    [Fact]
    public void NoConfigurationOptionsValueIsBuiltFromAString()
    {
        var sources = Directory.GetFiles(Path.Combine(RepoRoot(), "src", "Iskender.Infrastructure.Redis"), "*.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(sources);
        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            Assert.False(ParsedOptions().IsMatch(text), $"{Path.GetFileName(source)} parses client options from a string.");
        }

        // The one multiplexer is built from the field-by-field options alone.
        var connections = sources.Select(File.ReadAllText).SelectMany(text => Regex.Matches(text, @"ConnectionMultiplexer\s*\.\s*Connect\w*\s*\([^;]*").Select(match => match.Value)).ToList();
        Assert.Equal("ConnectionMultiplexer.ConnectAsync(options)", Assert.Single(connections));
        Assert.Single(sources.Select(File.ReadAllText), text => text.Contains("var options = Options(_settings, password);", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOptionsAreSetFieldByFieldFromTheSettings()
    {
        var settings = TestCoordinationSettings.Read(TestCoordinationSettings.Document(
            fixture,
            new Dictionary<string, object> { ["endpoints"] = TwoEndpoints, ["connectTimeout"] = "2s" },
            new Dictionary<string, object> { ["operationTimeout"] = "750ms" }));

        var options = RedisConnection.Options(settings, "resolved");

        Assert.Equal(["cache-1.internal:6390", "[::1]:6391"], options.EndPoints.Select(Format));
        Assert.Null(options.DefaultDatabase);
        Assert.Equal(fixture.Username, options.User);
        Assert.Equal("resolved", options.Password);
        Assert.False(options.Ssl);
        Assert.False(options.AbortOnConnectFail);
        Assert.Same(BacklogPolicy.FailFast, options.BacklogPolicy);
        Assert.Equal(2000, options.ConnectTimeout);
        Assert.Equal(750, options.AsyncTimeout);
        Assert.Equal(750, options.SyncTimeout);
        Assert.False(options.AllowAdmin);
        Assert.Null(options.LoggerFactory);

        // The client renders a disabled command as "$NAME=" in its options string, which here leaves the password out.
        var rendered = options.ToString(includePassword: false);
        Assert.DoesNotContain("resolved", rendered, StringComparison.Ordinal);
        Assert.All(RedisConnection.ExcludedCommands, command => Assert.Contains($"${command}=,", rendered + ",", StringComparison.Ordinal));
    }

    [Fact]
    public void RequireEnablesTlsWithChainAndHostNameValidation()
    {
        var options = RedisConnection.Options(RequireSettings(["cache-1.internal:6390"]), "resolved");

        Assert.True(options.Ssl);
        Assert.Null(options.SslHost);
        Assert.True(options.CheckCertificateRevocation);
        Assert.Equal(System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13, options.SslProtocols);
        using var certificate = SelfSigned();
        Assert.True(RedisConnection.ValidateServerCertificate(this, certificate, null, SslPolicyErrors.None));
        Assert.False(RedisConnection.ValidateServerCertificate(this, certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(RedisConnection.ValidateServerCertificate(this, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(RedisConnection.ValidateServerCertificate(this, null, null, SslPolicyErrors.None));
    }

    [Fact]
    public async Task RequireChecksEachEndpointsCertificateAgainstItsOwnHostName()
    {
        using var certificate = SelfSigned();
        using var first = new TcpListener(IPAddress.Loopback, 0);
        using var second = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        second.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = Task.WhenAll(ServeTlsAsync(first, certificate, stop.Token), ServeTlsAsync(second, certificate, stop.Token));
        string[] endpoints = [$"localhost:{((IPEndPoint)first.LocalEndpoint).Port}", $"127.0.0.1:{((IPEndPoint)second.LocalEndpoint).Port}"];
        var options = RedisConnection.Options(RequireSettings(endpoints), "resolved");
        var checkedHosts = new ConcurrentBag<string>();
        options.CertificateValidation += (sender, _, _, _) =>
        {
            checkedHosts.Add(((SslStream)sender).TargetHostName);
            return false;
        };

        await using (await ConnectionMultiplexer.ConnectAsync(options))
        {
            for (var waited = 0; checkedHosts.Distinct().Count() < 2 && waited < 50; waited++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            }
        }

        await stop.CancelAsync();
        await serving;
        Assert.Equal(["127.0.0.1", "localhost"], checkedHosts.Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RequireNeverFallsBackToAPlainConnection()
    {
        fixture.RequireRedis();
        await using var connection = new RedisConnection(RequireSettings([fixture.Endpoint]), TestCoordinationCredentialProvider.For(fixture));

        var failure = await Assert.ThrowsAsync<GatewayException>(() => Read(connection));

        Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
    }

    [Theory]
    [InlineData("cache-1.internal:6379", "cache-1.internal", 6379)]
    [InlineData("10.0.0.5:1", "10.0.0.5", 1)]
    [InlineData("[::1]:65535", "::1", 65535)]
    public void EndpointsAreParsedFromHostAndPort(string endpoint, string host, int port)
    {
        Assert.Equal((host, port), RedisConnection.ParseEndpoint(endpoint));
    }

    [Theory]
    [InlineData("cache-1.internal")]
    [InlineData("cache-1.internal:")]
    [InlineData(":6379")]
    [InlineData("cache-1.internal:0")]
    [InlineData("cache-1.internal:65536")]
    [InlineData("cache-1.internal:+6379")]
    [InlineData("[::1:6379")]
    [InlineData("[]:6379")]
    public void AMalformedEndpointFailsWithValueInvalid(string endpoint)
    {
        var failure = Assert.Throws<GatewayException>(() => RedisConnection.ParseEndpoint(endpoint));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.DoesNotContain(endpoint, failure.Message, StringComparison.Ordinal);
    }

    private Iskender.Infrastructure.Coordination.CoordinationSettings RequireSettings(string[] endpoints) => TestCoordinationSettings.Read(
        TestCoordinationSettings.Document(fixture, new Dictionary<string, object> { ["endpoints"] = endpoints, ["tlsMode"] = "require", ["connectTimeout"] = "1s" }),
        environment: "production");

    private static Task<RedisResult> Read(RedisConnection connection) =>
        connection.RunAsync(CoordinationScope.Operator, Counter.Module, CoordinationPrimitive.AtomicOperations, RedisScripts.Read, [RedisScripts.ValueKey(CoordinationScope.Operator, Counter)], [], CancellationToken.None).AsTask();

    // A user of its own with the adapter's rules, so changing its credential cannot disturb another test.
    private async Task<(string Username, string Password)> DedicatedUserAsync()
    {
        var username = "isk_t017_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var admin = await fixture.AdminDatabaseAsync();
        await admin.ExecuteAsync("ACL", ["SETUSER", username, "on", ">" + password, .. RedisFixture.AdapterRules]);
        return (username, password);
    }

    private static string Format(System.Net.EndPoint endpoint) => endpoint switch
    {
        System.Net.DnsEndPoint dns => $"{dns.Host}:{dns.Port}",
        System.Net.IPEndPoint { AddressFamily: System.Net.Sockets.AddressFamily.InterNetworkV6 } ip => $"[{ip.Address}]:{ip.Port}",
        _ => endpoint.ToString()!,
    };

    // Accepts connections and offers the certificate until stopped; the client's check refuses it, so no handshake completes.
    private static async Task ServeTlsAsync(TcpListener listener, X509Certificate2 certificate, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop);
                await using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException)
            {
                // The client refused the certificate.
            }
        }
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=cache-1.internal", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
    }

    private static string RepoRoot()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Iskender.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The repository root was not found.");
        }

        return root;
    }

    [GeneratedRegex(@"ConfigurationOptions\s*\.\s*Parse|ConnectionMultiplexer\s*\.\s*Connect(Async)?\s*\(\s*(\$|@)*""", RegexOptions.CultureInvariant)]
    private static partial Regex ParsedOptions();

    /// <summary>The system clock, moved forward on demand so a test can pass <c>connectTimeout</c> without waiting.</summary>
    private sealed class ShiftedTimeProvider : TimeProvider
    {
        private long _shift;

        public void Advance(TimeSpan by) => Interlocked.Add(ref _shift, (long)(by.TotalSeconds * TimestampFrequency) + 1);

        public override long GetTimestamp() => System.GetTimestamp() + Interlocked.Read(ref _shift);
    }
}
