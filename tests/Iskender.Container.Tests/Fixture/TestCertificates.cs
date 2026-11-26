using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// Self-signed certificates and their unencrypted PKCS #8 keys, generated at run time as PEM files in a temporary
/// directory this class deletes, readable by any user, so a container's non-root user can read them through a read-only
/// mount (container contract, Test fixtures rule 7). Nothing here is ever tracked.
/// </summary>
internal sealed class TestCertificates : IDisposable
{
    private const UnixFileMode Readable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>
    /// A certificate block whose content is not a certificate, put together at run time so that no tracked file holds a
    /// PEM block (ISK-0010 T051, FR-035).
    /// </summary>
    public static string MalformedCertificate { get; } =
        string.Join('\n', "-----BEGIN " + "CERTIFICATE-----", "bm90IGEgY2VydGlmaWNhdGU=", "-----END " + "CERTIFICATE-----", "");

    private readonly DirectoryInfo _directory = System.IO.Directory.CreateTempSubdirectory("isk-tls-");

    public TestCertificates()
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_directory.FullName, Readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>The directory that holds the files.</summary>
    public string Directory => _directory.FullName;

    /// <summary>A pair valid from five minutes ago for a day, as <c>{name}.crt.pem</c> and <c>{name}.key.pem</c>.</summary>
    public (string Certificate, string Key) Pair(string name = "pair") => Pair(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1), name);

    public (string Certificate, string Key) Pair(DateTimeOffset notBefore, DateTimeOffset notAfter, string name = "pair")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=iskender-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        var certificatePath = Write($"{name}.crt.pem", certificate.ExportCertificatePem());
        var keyPath = Write($"{name}.key.pem", key.ExportPkcs8PrivateKeyPem());
        return (certificatePath, keyPath);
    }

    /// <summary>
    /// A root, an intermediate it signs, and a leaf for <c>iskender-test</c> the intermediate signs, generated at run time:
    /// <c>{name}.crt.pem</c> holds the leaf then the intermediate, <c>{name}.key.pem</c> the leaf's key, and the root is
    /// returned, never written, so a client can trust it alone (ISK-0010 T063).
    /// </summary>
    public (string Certificate, string Key, X509Certificate2 Root, X509Certificate2 Intermediate) Chain(string name = "chain")
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=iskender-test-root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Authority(rootRequest);
        var root = rootRequest.CreateSelfSigned(notBefore, notBefore.AddDays(3));

        using var intermediateKey = RSA.Create(2048);
        var intermediateRequest = new CertificateRequest("CN=iskender-test-intermediate", intermediateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Authority(intermediateRequest);
        using var issued = intermediateRequest.Create(root, notBefore, notBefore.AddDays(2), [1, 2, 3, 4]);
        using var intermediate = issued.CopyWithPrivateKey(intermediateKey);

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=iskender-test", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("iskender-test");
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(leafRequest.PublicKey, false));
        using var leaf = leafRequest.Create(intermediate, notBefore, notBefore.AddDays(1), [5, 6, 7, 8]);

        var certificatePath = Write($"{name}.crt.pem", leaf.ExportCertificatePem() + "\n" + intermediate.ExportCertificatePem() + "\n");
        var keyPath = Write($"{name}.key.pem", leafKey.ExportPkcs8PrivateKeyPem());
        return (certificatePath, keyPath, root, X509CertificateLoader.LoadCertificate(intermediate.RawData));
    }

    private static void Authority(CertificateRequest request)
    {
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
    }

    /// <summary>Writes <paramref name="text"/> to <paramref name="file"/> in the directory, readable by any user.</summary>
    public string Write(string file, string text)
    {
        var path = Path.Combine(_directory.FullName, file);
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, Readable);
        }

        return path;
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
