using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Ports;

/// <summary>
/// The owner scope a unit of work runs in, fixed for its life. It has no public constructor and takes no
/// <see cref="OwnerScope"/>, so a scope built from caller values cannot open a unit (FR-005, research R4). It offers no
/// equality, because it is a capability, not a value. Owned by ISK-0004.
/// </summary>
public sealed class UnitOfWorkScope
{
    private UnitOfWorkScope(OwnerScope owner, DateTimeOffset? deadline, CancellationToken requestCancellation)
    {
        Owner = owner;
        Deadline = deadline;
        RequestCancellation = requestCancellation;
    }

    /// <summary>The operator scope, for system data.</summary>
    public static UnitOfWorkScope Operator { get; } = new(OperatorScope.Instance, deadline: null, CancellationToken.None);

    /// <summary>The owner scope.</summary>
    public OwnerScope Owner { get; }

    /// <summary>The request's deadline, or <c>null</c> for a unit not opened from a request.</summary>
    public DateTimeOffset? Deadline { get; }

    /// <summary>The request's cancellation token, or <see cref="CancellationToken.None"/>.</summary>
    public CancellationToken RequestCancellation { get; }

    /// <summary>The attested owner of a resource. A <c>null</c> reference fails with <c>persistence.scope_required</c>.</summary>
    public static UnitOfWorkScope ForResource(ScopedResourceReference reference) =>
        reference is null ? throw ScopeRequired() : new(reference.Owner, deadline: null, CancellationToken.None);

    /// <summary>
    /// The request's resolved owner, with its deadline and cancellation token. A <c>null</c> context, or one whose owner
    /// is not resolved, fails with <c>persistence.scope_required</c>.
    /// </summary>
    public static UnitOfWorkScope ForRequest(GatewayExecutionContext context)
    {
        if (context is null)
        {
            throw ScopeRequired();
        }

        OwnerScope owner;
        try
        {
            owner = context.Owner;
        }
        catch (GatewayException exception) when (ReferenceEquals(exception.Code, ErrorCodes.ContextNotResolved))
        {
            throw ScopeRequired();
        }

        return new(owner, context.Deadline, context.CancellationToken);
    }

    private static GatewayException ScopeRequired() => new(ErrorCodes.PersistenceScopeRequired);
}
