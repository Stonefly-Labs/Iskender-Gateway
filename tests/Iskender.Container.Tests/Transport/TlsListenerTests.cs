using System.Globalization;
using System.Text.RegularExpressions;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.Transport;

/// <summary>
/// <c>listener</c> mode on the images (ISK-0010 T038, US4.5, US4.6, SC-009, AV-8, container contract Test fixtures rule
/// 7, operator-security contract Transport rule 3): each plane's image, read-only and non-root, with a self-signed pair
/// generated at run time and mounted read-only, completes a TLS 1.2 and a TLS 1.3 handshake on its traffic port, the
/// control plane answers a redemption attempt over TLS 1.2, a plain-HTTP request to the traffic port runs no route, and
/// the health listener answers plain HTTP. A missing, malformed, encrypted, expired, or mismatched pair exits 2 with
/// <c>operator.certificate_unusable</c>, and the output holds neither the path nor any line of the file. The client is
/// curl in the SDK image the Dockerfiles build from, on the container's network, since the test host's TLS stack may
/// offer no TLS 1.3.
/// </summary>
[Trait("Category", "Compose")]
public sealed partial class TlsListenerTests(ImageBuild build) : IDisposable
{
    private const string Mount = "/etc/iskender-tls";

    private readonly TestCertificates _certificates = new();

    public static TheoryData<string> Planes() => ["data_plane", "control_plane"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _certificates.Dispose();

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task ListenerModeServesTls12AndTls13OnTheTrafficPortAndPlainHttpOnTheHealthPort(string plane)
    {
        _certificates.Pair();
        await using var running = await StartAsync(plane);

        Assert.Equal("TLSv1.2", await NegotiatedAsync(running, "--tlsv1.2", "--tls-max", "1.2"));
        Assert.Equal("TLSv1.3", await NegotiatedAsync(running, "--tlsv1.3", "--tls-max", "1.3"));
        Assert.Equal("000", await StatusAsync(running, $"http://{running.Name}:8080/admin/v1/installation"));
        Assert.Equal("200", await StatusAsync(running, $"http://{running.Name}:8081/health/live"));
        if (plane == "control_plane")
        {
            var redemption = await StatusAsync(
                running,
                $"https://{running.Name}:8080/admin/v1/bootstrap",
                "--insecure", "--tlsv1.2", "--tls-max", "1.2", "--request", "POST", "--header", "Content-Type: application/json", "--data", "{\"token\":\"x\"}");
            Assert.NotEqual("000", redemption);
            Assert.NotEqual("404", redemption);
        }
    }

    public static TheoryData<string, string> UnusablePairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var plane in new[] { "data_plane", "control_plane" })
        {
            foreach (var pair in new[] { "missing", "malformed", "encrypted", "expired", "mismatched" })
            {
                data.Add(plane, pair);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnusablePairs))]
    public async Task AnUnusablePairExits2NamingTheCodeAndFieldAndNeverThePathOrContent(string plane, string pair)
    {
        var (certificate, key) = pair == "expired"
            ? _certificates.Pair(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1))
            : _certificates.Pair();
        switch (pair)
        {
            case "missing":
                File.Delete(key);
                break;
            case "malformed":
                _certificates.Write(Path.GetFileName(certificate), TestCertificates.MalformedCertificate);
                break;
            case "encrypted":
                using (var rsa = System.Security.Cryptography.RSA.Create(2048))
                {
                    _certificates.Write(Path.GetFileName(key), rsa.ExportEncryptedPkcs8PrivateKeyPem(
                        "passphrase", new System.Security.Cryptography.PbeParameters(System.Security.Cryptography.PbeEncryptionAlgorithm.Aes256Cbc, System.Security.Cryptography.HashAlgorithmName.SHA256, 100_000)));
                }

                break;
            case "mismatched":
                _certificates.Write(Path.GetFileName(key), File.ReadAllText(_certificates.Pair("other").Key));
                break;
        }

        var lines = Directory.EnumerateFiles(_certificates.Directory).SelectMany(File.ReadAllLines).Where(line => line.Length > 0).ToList();
        var image = (await build.GetAsync()).For(plane);
        using var configuration = Configuration();

        var result = await DockerCli.RunAsync(["run", "--rm", "--read-only", "--tmpfs", "/tmp", .. configuration.Mount, .. TlsMount, image], TimeSpan.FromMinutes(2), Token);

        Assert.Equal(2, result.ExitCode);
        var output = result.StandardOutput + result.StandardError;
        Assert.Contains("operator.certificate_unusable", output, StringComparison.Ordinal);
        // Event 7002's reason names the failing field (Diagnostics rule 2; CONV-0010-003).
        var field = pair is "malformed" or "expired" ? "tlsCertificateFile" : "tlsKeyFile";
        Assert.Contains($"reason iskender.container.{field}.", output, StringComparison.Ordinal);
        Assert.DoesNotContain(Mount, output, StringComparison.Ordinal);
        Assert.DoesNotContain(_certificates.Directory, output, StringComparison.Ordinal);
        Assert.All(lines, line => Assert.DoesNotContain(line, output, StringComparison.Ordinal));
    }

    private string[] TlsMount => ["--volume", $"{_certificates.Directory}:{Mount}:ro"];

    private static MountedConfiguration Configuration() => new(
        "{\"iskender\":{\"container\":{\"trafficTransport\":\"listener\","
        + $"\"tlsCertificateFile\":\"{Mount}/pair.crt.pem\",\"tlsKeyFile\":\"{Mount}/pair.key.pem\"}},"
        + PlaneDefinitions.Persistence + "}}");

    private async Task<Running> StartAsync(string plane)
    {
        var image = (await build.GetAsync()).For(plane);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var running = new Running($"isk-tls-{suffix}", $"isk-tls-net-{suffix}", Configuration());
        await DockerAsync(["network", "create", running.Network]);
        await DockerAsync(["run", "--detach", "--name", running.Name, "--network", running.Network, "--read-only", "--tmpfs", "/tmp", .. running.Configuration.Mount, .. TlsMount, image]);
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await StatusAsync(running, $"http://{running.Name}:8081/health/live") == "200")
            {
                return running;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), Token);
        }

        var logs = await DockerCli.RunAsync(["logs", running.Name], TimeSpan.FromMinutes(1), Token);
        await running.DisposeAsync();
        Assert.Fail($"Nothing answered live within 60 seconds: {logs.StandardOutput}{logs.StandardError}");
        return running;
    }

    // The protocol curl negotiated for an HTTPS request to the traffic port, from its verbose output.
    private static async Task<string> NegotiatedAsync(Running running, params string[] protocol)
    {
        var result = await CurlAsync(running, ["--insecure", "--verbose", "--output", "/dev/null", .. protocol, $"https://{running.Name}:8080/"]);
        var match = Negotiated().Match(result.StandardError);
        Assert.True(match.Success, result.StandardError);
        return match.Groups["protocol"].Value;
    }

    // The HTTP status curl reports, or 000 when no response arrived.
    private static async Task<string> StatusAsync(Running running, string url, params string[] options)
    {
        var result = await CurlAsync(running, ["--silent", "--output", "/dev/null", "--write-out", "%{http_code}", "--max-time", "5", .. options, url]);
        return result.StandardOutput.Trim();
    }

    private static async Task<DockerResult> CurlAsync(Running running, string[] arguments) =>
        await DockerCli.RunAsync(["run", "--rm", "--network", running.Network, await SdkImageAsync(), "curl", .. arguments], TimeSpan.FromMinutes(2), Token);

    // The SDK image the control plane's Dockerfile builds from, digest-pinned there.
    private static async Task<string> SdkImageAsync()
    {
        var dockerfile = await File.ReadAllTextAsync(Path.Combine(await ImageBuild.RepositoryRootAsync(), "src/Iskender.ControlPlane.Host/Dockerfile"), Token);
        return SdkImage().Match(dockerfile).Value;
    }

    private static async Task DockerAsync(IReadOnlyList<string> arguments)
    {
        var result = await DockerCli.RunAsync(arguments, TimeSpan.FromMinutes(2), Token);
        Assert.True(result.ExitCode == 0, string.Create(CultureInfo.InvariantCulture, $"docker {arguments[0]} exited {result.ExitCode}: {result.StandardError}"));
    }

    [GeneratedRegex(@"SSL connection using (?<protocol>TLSv1\.[0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex Negotiated();

    [GeneratedRegex(@"mcr\.microsoft\.com/dotnet/sdk:[^\s]+@sha256:[0-9a-f]{64}", RegexOptions.CultureInvariant)]
    private static partial Regex SdkImage();

    private sealed record Running(string Name, string Network, MountedConfiguration Configuration) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await DockerCli.RunAsync(["rm", "--force", Name], TimeSpan.FromMinutes(1), CancellationToken.None);
            await DockerCli.RunAsync(["network", "rm", Network], TimeSpan.FromMinutes(1), CancellationToken.None);
            Configuration.Dispose();
        }
    }
}
