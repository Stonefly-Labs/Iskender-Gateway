namespace Iskender.Infrastructure.Container;

/// <summary>
/// How a plane's traffic listener is protected (container contract, The <c>container</c> section and Listeners rule 1;
/// research R13). Owned by ISK-0010.
/// </summary>
public enum TrafficTransport
{
    /// <summary>Code <c>listener</c>: in-process TLS from the PEM files of <c>tlsCertificateFile</c> and <c>tlsKeyFile</c>.</summary>
    Listener,

    /// <summary>Code <c>upstream</c>: plain HTTP, the operator declaring that TLS is terminated in front of the container.</summary>
    Upstream,

    /// <summary>Code <c>none</c>: plain HTTP with no declaration, permitted only in <c>development</c>.</summary>
    None,
}
