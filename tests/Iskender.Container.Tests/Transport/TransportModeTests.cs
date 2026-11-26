using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Transport;

/// <summary>
/// Transport modes (ISK-0010 T038, US4.5, SC-009, container contract Test fixtures rule 6, operator-security contract,
/// Transport rules 1 and 2): each plane started without an environment name with <c>trafficTransport: none</c> exits 2
/// with <c>operator.transport_unprotected</c> and binds no listener, each unusable certificate pair exits 2 naming its
/// field in event 7002 and binds no listener, <c>upstream</c> and <c>listener</c> start, and the worker ignores the fields.
/// </summary>
public sealed class TransportModeTests : IDisposable
{
    private const string Leases = ",\"coordination\":{\"leases\":{\"coordinator\":\"postgresql\"}}";

    private readonly TestCertificates _certificates = new();

    public static TheoryData<string> Planes() => ["data-plane", "control-plane"];

    public void Dispose() => _certificates.Dispose();

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task NoneWithoutAnEnvironmentNameExits2AndBindsNoListener(string plane)
    {
        using var host = new ContainerTestHost().WithDocument("\"trafficTransport\":\"none\"", PlaneDefinitions.Persistence);

        var exitCode = await host.RunAsync(Definition(plane, host));

        Assert.Equal(ExitCodes.ConfigurationRejected, exitCode);
        Assert.Equal("operator.transport_unprotected", host.Log.Only(7002).Value("Code"));
        Assert.Equal("iskender.container.trafficTransport", host.Log.Only(7002).Value("Reason"));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.TrafficPort));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.HealthPort));
    }

    public static TheoryData<string, string, string> CertificateFailures()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var plane in new[] { "data-plane", "control-plane" })
        {
            data.Add(plane, "missing-certificate", "tlsCertificateFile");
            data.Add(plane, "missing-key", "tlsKeyFile");
            data.Add(plane, "malformed-certificate", "tlsCertificateFile");
            data.Add(plane, "expired", "tlsCertificateFile");
            data.Add(plane, "mismatched", "tlsKeyFile");
        }

        return data;
    }

    // Each unusable pair exits 2 with event 7002 naming the code and, as its reason, the failing field, never the file's
    // path or content, and binds no listener (FR-022a, US4.5, SC-009, Diagnostics rule 2; CONV-0010-003, CONV-0010-010).
    [Theory]
    [MemberData(nameof(CertificateFailures))]
    public async Task AnUnusablePairExits2NamingTheFieldInEvent7002AndBindsNoListener(string plane, string failure, string field)
    {
        var (certificate, key) = failure == "expired"
            ? _certificates.Pair(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1))
            : _certificates.Pair();
        switch (failure)
        {
            case "missing-certificate":
                File.Delete(certificate);
                break;
            case "missing-key":
                File.Delete(key);
                break;
            case "malformed-certificate":
                _certificates.Write(Path.GetFileName(certificate), TestCertificates.MalformedCertificate);
                break;
            case "mismatched":
                _certificates.Write(Path.GetFileName(key), File.ReadAllText(_certificates.Pair("other").Key));
                break;
        }

        using var host = new ContainerTestHost().WithDocument(Listener(certificate, key), PlaneDefinitions.Persistence);

        var exitCode = await host.RunAsync(Definition(plane, host));

        Assert.Equal(ExitCodes.ConfigurationRejected, exitCode);
        var refused = host.Log.Only(7002);
        Assert.Equal("operator.certificate_unusable", refused.Value("Code"));
        Assert.Equal("iskender.container." + field, refused.Value("Reason"));
        Assert.All(host.Log.Entries, entry => Assert.DoesNotContain(_certificates.Directory, entry.Message, StringComparison.Ordinal));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.TrafficPort));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.HealthPort));
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task UpstreamStartsWithPlainHttpOnTheTrafficListener(string plane)
    {
        using var host = new ContainerTestHost().WithDocument("\"trafficTransport\":\"upstream\"", PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(Definition(plane, host));
        await host.WhenStartedAsync(client);

        // A plain-HTTP request reaches the routes: the data plane's catch-all, and on the control plane no route.
        using var response = await client.GetAsync($"http://127.0.0.1:{host.TrafficPort}/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task ListenerStartsWithTlsOnTheTrafficListenerAndPlainHttpOnTheHealthListener(string plane)
    {
        var (certificate, key) = _certificates.Pair();
        using var host = new ContainerTestHost().WithDocument(Listener(certificate, key), PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(Definition(plane, host));
        await host.WhenStartedAsync(client);

        using var expected = X509Certificate2.CreateFromPem(File.ReadAllText(certificate));
        Assert.Equal(SslProtocols.Tls12, await HandshakeAsync(host.TrafficPort, SslProtocols.Tls12, expected.GetCertHashString()));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Theory]
    [InlineData("\"trafficTransport\":\"none\"")]
    [InlineData("\"trafficTransport\":\"listener\",\"tlsCertificateFile\":\"/missing/certificate.pem\",\"tlsKeyFile\":\"/missing/key.pem\"")]
    [InlineData("\"trafficTransport\":\"upstream\",\"tlsKeyFile\":\"/missing/key.pem\"")]
    public async Task TheWorkerIgnoresTheTransportFields(string fields)
    {
        using var host = new ContainerTestHost().WithDocument(fields, PlaneDefinitions.Persistence + Leases);
        using var client = new HttpClient();
        var run = await host.StartAsync(PlaneDefinitions.ControlPlane(host.AddStubs), "work");
        await host.WhenStartedAsync(client);

        Assert.Empty(host.Log.ById(7002));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.TrafficPort));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    // A handshake that accepts only the generated certificate, which is self-signed and so has no chain to validate.
    internal static async Task<SslProtocols> HandshakeAsync(int port, SslProtocols protocol, string thumbprint)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port, TestContext.Current.CancellationToken);
        await using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, presented, _, _) => presented?.GetCertHashString() == thumbprint);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "iskender-test", EnabledSslProtocols = protocol }, TestContext.Current.CancellationToken);
        return tls.SslProtocol;
    }

    private static ContainerPlaneDefinition Definition(string plane, ContainerTestHost host) =>
        plane == "data-plane" ? PlaneDefinitions.DataPlane(host.AddStubs) : PlaneDefinitions.ControlPlane(host.AddStubs);

    private static string Listener(string certificate, string key) =>
        $"\"trafficTransport\":\"listener\",\"tlsCertificateFile\":{JsonSerializer.Serialize(certificate)},\"tlsKeyFile\":{JsonSerializer.Serialize(key)}";
}
