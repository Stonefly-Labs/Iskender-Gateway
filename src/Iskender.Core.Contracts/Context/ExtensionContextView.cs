using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Context;

/// <summary>
/// What an extension may see of a request: read-only, with no way to resolve a value, and no service provider, storage
/// handle, secret, or state shared across requests (FR-017). Unresolved values fail as they do on the context.
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class ExtensionContextView
{
    private readonly GatewayExecutionContext _context;

    internal ExtensionContextView(GatewayExecutionContext context)
    {
        _context = context;
    }

    /// <summary>The resolved owner scope.</summary>
    public OwnerScope Owner => _context.Owner;

    /// <summary>The resolved endpoint.</summary>
    public ScopedResourceReference Endpoint => _context.Endpoint;

    /// <summary>The resolved configuration revision.</summary>
    public ConfigurationRevision ConfigurationRevision => _context.ConfigurationRevision;

    /// <summary>When the request must complete.</summary>
    public DateTimeOffset Deadline => _context.Deadline;

    /// <summary>Signals that the request was cancelled.</summary>
    public CancellationToken CancellationToken => _context.CancellationToken;

    /// <summary>The request's correlation values.</summary>
    public CorrelationContext Correlation => _context.Correlation;
}
