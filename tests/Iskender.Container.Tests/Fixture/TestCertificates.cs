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
