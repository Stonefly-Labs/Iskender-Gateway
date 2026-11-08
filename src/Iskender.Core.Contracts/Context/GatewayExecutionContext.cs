using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Context;

/// <summary>
/// The context of one request, created by the host and never shared. The owner scope, endpoint, and configuration
/// revision are each resolved once (FR-014). There is no static or ambient state (FR-018). Members later specs own are
/// reached through sealed views (FR-015), and extensions get the read-only <see cref="ExtensionContextView"/> (FR-017).
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class GatewayExecutionContext
{
    private readonly TimeProvider _timeProvider;
    private OwnerScope? _owner;
    private ScopedResourceReference? _endpoint;
    private ConfigurationRevision? _configurationRevision;

    public GatewayExecutionContext(CorrelationContext correlation, DateTimeOffset deadline, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        Correlation = correlation ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        Deadline = deadline;
        CancellationToken = cancellationToken;
    }

    /// <summary>The request's correlation values.</summary>
    public CorrelationContext Correlation { get; }

    /// <summary>When the request must complete.</summary>
    public DateTimeOffset Deadline { get; }

    /// <summary>Signals that the caller or the gateway cancelled the request.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>True once the current time is at or past <see cref="Deadline"/>, including at creation (FR-019).</summary>
    public bool IsExpired => _timeProvider.GetUtcNow() >= Deadline;

    /// <summary>The resolved owner scope. Fails with <c>core.context.not_resolved</c> before <see cref="ResolveOwner"/>.</summary>
    public OwnerScope Owner => Volatile.Read(ref _owner) ?? throw NotResolved();

    /// <summary>The resolved endpoint. Fails with <c>core.context.not_resolved</c> before <see cref="ResolveEndpoint"/>.</summary>
    public ScopedResourceReference Endpoint => Volatile.Read(ref _endpoint) ?? throw NotResolved();

    /// <summary>The resolved configuration revision. Fails with <c>core.context.not_resolved</c> before <see cref="ResolveConfigurationRevision"/>.</summary>
    public ConfigurationRevision ConfigurationRevision => Volatile.Read(ref _configurationRevision) ?? throw NotResolved();

    /// <summary>The caller's principal, owned by the identity specs.</summary>
    public PrincipalView Principal { get; } = new();

    /// <summary>The routing attempts, owned by the routing specs.</summary>
    public RoutingAttemptView RoutingAttempts { get; } = new();

    /// <summary>The budget reservations, owned by the governance specs.</summary>
    public BudgetReservationView BudgetReservations { get; } = new();

    /// <summary>The policy execution, owned by the policy specs.</summary>
    public PolicyExecutionView PolicyExecution { get; } = new();

    /// <summary>Sets the owner scope once. A second call fails with <c>core.context.already_resolved</c>.</summary>
    public void ResolveOwner(OwnerScope owner) => Resolve(ref _owner, owner);

    /// <summary>
    /// Sets the endpoint once. A reference whose ID is not an endpoint fails with <c>core.resource_id.wrong_type</c>, and
    /// a second call fails with <c>core.context.already_resolved</c>.
    /// </summary>
    public void ResolveEndpoint(ScopedResourceReference endpoint)
    {
        if (endpoint is not null && !ReferenceEquals(endpoint.Id.Type, ResourceTypes.Endpoint))
        {
            throw new GatewayException(ErrorCodes.ResourceIdWrongType);
        }

        Resolve(ref _endpoint, endpoint);
    }

    /// <summary>Sets the configuration revision once. A second call fails with <c>core.context.already_resolved</c>.</summary>
    public void ResolveConfigurationRevision(ConfigurationRevision revision) => Resolve(ref _configurationRevision, revision);

    /// <summary>A read-only view for extensions.</summary>
    public ExtensionContextView ForExtension() => new(this);

    private static void Resolve<T>(ref T? slot, T? value)
        where T : class
    {
        if (value is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (Interlocked.CompareExchange(ref slot, value, null) is not null)
        {
            throw new GatewayException(ErrorCodes.ContextAlreadyResolved);
        }
    }

    private static GatewayException NotResolved() => new(ErrorCodes.ContextNotResolved);
}
