using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Settings;

/// <summary>
/// The <c>container</c> section's transport fields (ISK-0010 T032, FR-022, FR-022a, research R13, container contract The
/// <c>container</c> section rules 1 to 3): each mode with and without an environment name, each file-field rule, each
/// unusable pair, and a definition with no traffic listener, which ignores them. Certificates and keys are generated at
/// run time into a temporary directory this class deletes.
/// </summary>
public sealed class TrafficTransportSettingsTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("isk-tls-");

    public void Dispose() => _directory.Delete(recursive: true);

    public static TheoryData<string, string?, string?> Modes() => new()
    {
        { "none", null, "operator.transport_unprotected" },
        { "none", "staging", "operator.transport_unprotected" },
        { "none", "development", null },
        { "upstream", null, null },
        { "upstream", "development", null },
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void EachPlainModeStartsOnlyWhereItIsAllowed(string mode, string? environment, string? code)
    {
        var fields = $"\"trafficTransport\":\"{mode}\"";
        if (code is null)
        {
            var settings = Read(fields, environment);
            Assert.Equal(mode == "none" ? TrafficTransport.None : TrafficTransport.Upstream, settings.TrafficTransport);
            Assert.Null(settings.TlsCertificate);
            return;
        }

        AssertRefused(fields, environment, code, "trafficTransport");
    }

    [Fact]
    public void TheDefaultIsNoneSoADocumentThatSaysNothingFailsOutsideDevelopment() =>
        AssertRefused("", environment: null, "operator.transport_unprotected", "trafficTransport");

    [Fact]
    public void ListenerModeLoadsTheCertificateAndItsKeyOnce()
    {
        var (certificate, key) = Pair();

        using var loaded = Read(Listener(certificate, key), environment: null).TlsCertificate;

        Assert.NotNull(loaded);
        Assert.True(loaded.HasPrivateKey);
        Assert.Equal("CN=iskender-test", loaded.Subject);
    }

    public static TheoryData<string, string> FileFieldRules() => new()
    {
        { "listener-no-certificate", "tlsCertificateFile" },
        { "listener-no-key", "tlsKeyFile" },
        { "listener-relative-certificate", "tlsCertificateFile" },
        { "listener-relative-key", "tlsKeyFile" },
        { "upstream-with-certificate", "tlsCertificateFile" },
        { "upstream-with-key", "tlsKeyFile" },
        { "none-with-key", "tlsKeyFile" },
    };

    [Theory]
    [MemberData(nameof(FileFieldRules))]
    public void AFileFieldMissingInListenerModeOrSetInAnotherIsRefused(string rule, string field)
    {
        var (certificate, key) = Pair();
        var fields = rule switch
        {
            "listener-no-certificate" => $"\"trafficTransport\":\"listener\",\"tlsKeyFile\":{Json(key)}",
            "listener-no-key" => $"\"trafficTransport\":\"listener\",\"tlsCertificateFile\":{Json(certificate)}",
            "listener-relative-certificate" => Listener("certificate.pem", key),
            "listener-relative-key" => Listener(certificate, "key.pem"),
            "upstream-with-certificate" => $"\"trafficTransport\":\"upstream\",\"tlsCertificateFile\":{Json(certificate)}",
            "upstream-with-key" => $"\"trafficTransport\":\"upstream\",\"tlsKeyFile\":{Json(key)}",
            _ => $"\"trafficTransport\":\"none\",\"tlsKeyFile\":{Json(key)}",
        };

        AssertRefused(fields, rule.StartsWith("none", StringComparison.Ordinal) ? "development" : null, "operator.certificate_unusable", field);
    }

    public static TheoryData<string, string> UnusablePairs() => new()
    {
        { "missing-certificate", "tlsCertificateFile" },
        { "missing-key", "tlsKeyFile" },
        { "unreadable-certificate", "tlsCertificateFile" },
        { "malformed-certificate", "tlsCertificateFile" },
        { "malformed-key", "tlsKeyFile" },
        { "encrypted-key", "tlsKeyFile" },
        { "not-yet-valid", "tlsCertificateFile" },
        { "expired", "tlsCertificateFile" },
        { "mismatched", "tlsKeyFile" },
    };

    [Theory]
    [MemberData(nameof(UnusablePairs))]
    public void AnUnusablePairIsRefusedNamingTheFieldAndNeverItsPathOrContent(string pair, string field)
    {
        if (pair == "unreadable-certificate" && (OperatingSystem.IsWindows() || Environment.UserName == "root"))
        {
            Assert.Skip("File modes do not stop this user from reading a file.");
        }

        var (certificate, key) = pair switch
        {
            "not-yet-valid" => Pair(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2)),
            "expired" => Pair(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1)),
            _ => Pair(),
        };
        switch (pair)
        {
            case "missing-certificate":
                File.Delete(certificate);
                break;
            case "missing-key":
                File.Delete(key);
                break;
            case "unreadable-certificate" when !OperatingSystem.IsWindows():
                File.SetUnixFileMode(certificate, UnixFileMode.None);
                break;
            case "malformed-certificate":
                File.WriteAllText(certificate, TestCertificates.MalformedCertificate);
                break;
            case "malformed-key":
                File.WriteAllText(key, "not a key\n");
                break;
            case "encrypted-key":
                using (var encrypted = RSA.Create(2048))
                {
                    File.WriteAllText(key, encrypted.ExportEncryptedPkcs8PrivateKeyPem("passphrase", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000)));
                }

                break;
            case "mismatched":
                File.Copy(Pair("other").Key, key, overwrite: true);
                break;
        }

        var failure = AssertRefused(Listener(certificate, key), environment: null, "operator.certificate_unusable", field);

        var text = failure.ToString() + string.Join(",", failure.Error!.Diagnostics.Values.Select(value => value.Value));
        Assert.DoesNotContain(_directory.FullName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", text, StringComparison.Ordinal);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public void ADefinitionWithNoTrafficListenerIgnoresTheFields()
    {
        var (certificate, _) = Pair();

        foreach (var fields in new[] { "", "\"trafficTransport\":\"listener\"", $"\"trafficTransport\":\"upstream\",\"tlsCertificateFile\":{Json(certificate)}" })
        {
            var settings = Read(fields, environment: null, hasTrafficListener: false);

            Assert.Equal(TrafficTransport.None, settings.TrafficTransport);
            Assert.Null(settings.TlsCertificate);
        }
    }

    private static ConfigurationSection Section(string fields, string? environment)
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([ContainerSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("container.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"container\":{" + fields + "}}}"),
            environmentName: environment));
        Assert.True(report.IsValid, report.ToString());
        return report.Snapshot!.GetSection(ContainerSectionSchema.Schema);
    }

    private static ContainerSettings Read(string fields, string? environment, bool hasTrafficListener = true) =>
        ContainerSettings.From(Section(fields, environment), environment, hasTrafficListener);

    private static GatewayException AssertRefused(string fields, string? environment, string code, string field)
    {
        var section = Section(fields, environment);
        var failure = Assert.Throws<GatewayException>(() => ContainerSettings.From(section, environment, hasTrafficListener: true));
        Assert.Equal(code, failure.Code.Id);
        Assert.Equal(["iskender.container." + field], failure.Error!.Diagnostics.Values.Where(value => value.Key == "field").Select(value => value.Value));
        return failure;
    }

    private static string Listener(string certificate, string key) =>
        $"\"trafficTransport\":\"listener\",\"tlsCertificateFile\":{Json(certificate)},\"tlsKeyFile\":{Json(key)}";

    private static string Json(string text) => JsonSerializer.Serialize(text);

    // A self-signed certificate and its unencrypted PKCS #8 key, as PEM files in this class's directory.
    private (string Certificate, string Key) Pair(string name = "pair") => Pair(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1), name);

    private (string Certificate, string Key) Pair(DateTimeOffset notBefore, DateTimeOffset notAfter, string name = "pair")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=iskender-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        var certificatePath = Path.Combine(_directory.FullName, $"{name}.crt.pem");
        var keyPath = Path.Combine(_directory.FullName, $"{name}.key.pem");
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        return (certificatePath, keyPath);
    }
}
