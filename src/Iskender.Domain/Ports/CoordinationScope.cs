using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Ports;

/// <summary>
/// The owner scope a coordination operation runs in (FR-003, research R4). It wraps a <see cref="UnitOfWorkScope"/>, so
/// it has exactly the trust rules persistence has, and the PostgreSQL coordinator opens its unit from the same scope
/// without widening it. It has no public constructor and takes no <see cref="OwnerScope"/>, and it offers no equality,
/// because it is a capability, not a value. Owned by ISK-0005.
/// </summary>
public sealed class CoordinationScope
{
    private CoordinationScope(UnitOfWorkScope unitScope)
    {
        UnitScope = unitScope;
    }

    /// <summary>The operator scope, for system state such as the instance lease.</summary>
    public static CoordinationScope Operator { get; } = new(UnitOfWorkScope.Operator);

    /// <summary>The unit of work scope it wraps.</summary>
    public UnitOfWorkScope UnitScope { get; }

    /// <summary>The owner scope.</summary>
    public OwnerScope Owner => UnitScope.Owner;

    /// <summary>The request's deadline, or <c>null</c> for a scope not taken from a request.</summary>
    public DateTimeOffset? Deadline => UnitScope.Deadline;

    /// <summary>The request's cancellation token, or <see cref="CancellationToken.None"/>.</summary>
    public CancellationToken RequestCancellation => UnitScope.RequestCancellation;

    /// <summary>The attested owner of a resource. A <c>null</c> reference fails with <c>coordination.scope_required</c>.</summary>
    public static CoordinationScope ForResource(ScopedResourceReference reference) =>
        reference is null ? throw ScopeRequired() : new(UnitOfWorkScope.ForResource(reference));

    /// <summary>
    /// The request's resolved owner, with its deadline and cancellation token. A <c>null</c> context, or one whose owner
    /// is not resolved, fails with <c>coordination.scope_required</c>.
    /// </summary>
    public static CoordinationScope ForRequest(GatewayExecutionContext context)
    {
        if (context is null)
        {
            throw ScopeRequired();
        }

        try
        {
            return new(UnitOfWorkScope.ForRequest(context));
        }
        catch (GatewayException exception) when (ReferenceEquals(exception.Code, ErrorCodes.PersistenceScopeRequired))
        {
            throw ScopeRequired();
        }
    }

    /// <summary>The scope of an open unit of work. A <c>null</c> scope fails with <c>coordination.scope_required</c>.</summary>
    public static CoordinationScope From(UnitOfWorkScope scope) => scope is null ? throw ScopeRequired() : new(scope);

    private static GatewayException ScopeRequired() => new(ErrorCodes.CoordinationScopeRequired);
}
