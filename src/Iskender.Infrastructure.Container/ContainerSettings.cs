using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Infrastructure.Container;

/// <summary>
/// The validated <c>iskender.container</c> section, read once (container contract, The <c>container</c> section). For a
/// plane with a traffic listener it also checks the transport fields and, in <c>listener</c> mode, loads the certificate
/// and key once (research R13, ISK-0010). Owned by ISK-0007.
/// </summary>
public sealed class ContainerSettings
{
    private const string Development = "development";

    internal ContainerSettings(
        int trafficPort,
        int healthPort,
        TimeSpan shutdownDelay,
        TimeSpan shutdownTimeout,
        TimeSpan readinessCacheDuration,
        TrafficTransport trafficTransport = TrafficTransport.None,
        X509Certificate2? tlsCertificate = null)
    {
        TrafficPort = trafficPort;
        HealthPort = healthPort;
        ShutdownDelay = shutdownDelay;
        ShutdownTimeout = shutdownTimeout;
        ReadinessCacheDuration = readinessCacheDuration;
        TrafficTransport = trafficTransport;
        TlsCertificate = tlsCertificate;
    }

    /// <summary>The port of the traffic listener.</summary>
    public int TrafficPort { get; }

    /// <summary>The port of the health listener.</summary>
    public int HealthPort { get; }

    /// <summary>How long requests keep being served after a stop signal, so load balancers can drain.</summary>
    public TimeSpan ShutdownDelay { get; }

    /// <summary>How long the server waits for requests in flight once it stops accepting connections.</summary>
    public TimeSpan ShutdownTimeout { get; }

    /// <summary>How long a readiness probe's result is reused.</summary>
    public TimeSpan ReadinessCacheDuration { get; }

    /// <summary>How the traffic listener is protected (ISK-0010).</summary>
    public TrafficTransport TrafficTransport { get; }

    /// <summary>The traffic listener's certificate, with its private key, in <c>listener</c> mode; otherwise <c>null</c> (ISK-0010).</summary>
    public X509Certificate2? TlsCertificate { get; }

    /// <summary>
    /// Reads a validated section. <c>trafficPort</c> equal to <c>healthPort</c> fails with <c>config.field.out_of_range</c>,
    /// naming <c>healthPort</c> in the error's diagnostics (research R9). Only when <paramref name="hasTrafficListener"/>,
    /// the transport fields are checked (ISK-0010, research R13): <c>none</c> when <paramref name="environmentName"/> is not
    /// <c>development</c> fails with <c>operator.transport_unprotected</c>; a file field that is missing in
    /// <c>listener</c> mode, set in another mode, or not an absolute path fails with <c>operator.certificate_unusable</c>;
    /// and in <c>listener</c> mode a pair that is missing, unreadable, malformed, encrypted, not yet valid, expired, or
    /// mismatched fails the same way. Each failure names the field in its diagnostics, never a path or file content.
    /// </summary>
    public static ContainerSettings From(ConfigurationSection section, string? environmentName, bool hasTrafficListener)
    {
        if (section is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var trafficPort = (int)section.GetInt64("trafficPort")!.Value;
        var healthPort = (int)section.GetInt64("healthPort")!.Value;
        if (trafficPort == healthPort)
        {
            throw Failure(ErrorCodes.ConfigFieldOutOfRange, section, "healthPort");
        }

        var transport = TrafficTransport.None;
        X509Certificate2? certificate = null;
        if (hasTrafficListener)
        {
            transport = Transport(section.GetString("trafficTransport"));
            if (transport == TrafficTransport.None && !string.Equals(environmentName, Development, StringComparison.Ordinal))
            {
                throw Failure(ErrorCodes.OperatorTransportUnprotected, section, "trafficTransport");
            }

            var certificateFile = section.GetString("tlsCertificateFile");
            var keyFile = section.GetString("tlsKeyFile");
            if (transport == TrafficTransport.Listener)
            {
                certificate = Load(section, Required(section, "tlsCertificateFile", certificateFile), Required(section, "tlsKeyFile", keyFile));
            }
            else if (certificateFile is not null || keyFile is not null)
            {
                throw Failure(ErrorCodes.OperatorCertificateUnusable, section, certificateFile is not null ? "tlsCertificateFile" : "tlsKeyFile");
            }
        }

        return new(
            trafficPort,
            healthPort,
            section.GetDuration("shutdownDelay")!.Value,
            section.GetDuration("shutdownTimeout")!.Value,
            section.GetDuration("readinessCacheDuration")!.Value,
            transport,
            certificate);
    }

    private static TrafficTransport Transport(string? code) => code switch
    {
        "listener" => TrafficTransport.Listener,
        "upstream" => TrafficTransport.Upstream,
        _ => TrafficTransport.None,
    };

    private static string Required(ConfigurationSection section, string field, string? path) =>
        path is not null && Path.IsPathFullyQualified(path) ? path : throw Failure(ErrorCodes.OperatorCertificateUnusable, section, field);

    // The certificate is read and checked alone first, so a failure names the file at fault; the pair is then loaded once.
    private static X509Certificate2 Load(ConfigurationSection section, string certificateFile, string keyFile)
    {
        var certificateText = Read(section, "tlsCertificateFile", certificateFile);
        var keyText = Read(section, "tlsKeyFile", keyFile);
        using (var alone = Parse(section, "tlsCertificateFile", () => X509Certificate2.CreateFromPem(certificateText)))
        {
            var now = DateTime.UtcNow;
            if (now < alone.NotBefore.ToUniversalTime() || now > alone.NotAfter.ToUniversalTime())
            {
                throw Failure(ErrorCodes.OperatorCertificateUnusable, section, "tlsCertificateFile");
            }
        }

        if (keyText.Contains("ENCRYPTED", StringComparison.Ordinal))
        {
            throw Failure(ErrorCodes.OperatorCertificateUnusable, section, "tlsKeyFile");
        }

        var pair = Parse(section, "tlsKeyFile", () => X509Certificate2.CreateFromPem(certificateText, keyText));
        if (!pair.HasPrivateKey)
        {
            pair.Dispose();
            throw Failure(ErrorCodes.OperatorCertificateUnusable, section, "tlsKeyFile");
        }

        return pair;
    }

    private static string Read(ConfigurationSection section, string field, string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw Failure(ErrorCodes.OperatorCertificateUnusable, section, field);
        }
    }

    private static X509Certificate2 Parse(ConfigurationSection section, string field, Func<X509Certificate2> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception failure) when (failure is CryptographicException or ArgumentException)
        {
            throw Failure(ErrorCodes.OperatorCertificateUnusable, section, field);
        }
    }

    private static GatewayException Failure(ErrorCode code, ConfigurationSection section, string field) =>
        new(GatewayError.Create(
            code,
            RequestCorrelationId.New(),
            OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(values: [new("field", $"{section.Path}.{field}")])));
}
