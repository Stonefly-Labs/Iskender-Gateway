using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Identity;

/// <summary>Who performs an audited operator operation. Owned by ISK-0010.</summary>
public enum OperatorActorKind
{
    /// <summary>An unauthenticated caller, such as a redemption.</summary>
    Anonymous,

    /// <summary>The control plane's <c>bootstrap</c> command.</summary>
    OperatorCommand,

    /// <summary>An operator principal.</summary>
    Principal,
}

/// <summary>
/// The actor of an audited operator operation. <see cref="Principal"/> is set exactly when <see cref="Kind"/> is
/// <see cref="OperatorActorKind.Principal"/>. Owned by ISK-0010.
/// </summary>
public sealed record OperatorActor
{
    private OperatorActor(OperatorActorKind kind, ResourceId? principal)
    {
        Kind = kind;
        Principal = principal;
    }

    public static OperatorActor Anonymous { get; } = new(OperatorActorKind.Anonymous, principal: null);

    public static OperatorActor OperatorCommand { get; } = new(OperatorActorKind.OperatorCommand, principal: null);

    public OperatorActorKind Kind { get; }

    public ResourceId? Principal { get; }

    /// <summary>An operator principal. A <c>null</c> ID, or one not of type <c>principal</c>, fails with <c>core.value.invalid</c>.</summary>
    public static OperatorActor ForPrincipal(ResourceId principal) =>
        principal is null || !ReferenceEquals(principal.Type, ResourceTypes.Principal)
            ? throw new GatewayException(ErrorCodes.ValueInvalid)
            : new(OperatorActorKind.Principal, principal);
}
