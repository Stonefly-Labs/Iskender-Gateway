using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Telemetry;

/// <summary>Who an audit record names as its actor. Owned by ISK-0010.</summary>
public enum AuditActorKind
{
    /// <summary>Stored as <c>anonymous</c>.</summary>
    Anonymous,

    /// <summary>Stored as <c>operator_command</c>: the control plane's <c>bootstrap</c> command.</summary>
    OperatorCommand,

    /// <summary>Stored as the principal's ID.</summary>
    Principal,
}

/// <summary>
/// An audit record's actor (operator-security contract, Audit records rule 2). <see cref="Principal"/> is set exactly when
/// <see cref="Kind"/> is <see cref="AuditActorKind.Principal"/>. Owned by ISK-0010.
/// </summary>
public sealed record AuditActor
{
    private AuditActor(AuditActorKind kind, ResourceId? principal)
    {
        Kind = kind;
        Principal = principal;
    }

    public static AuditActor Anonymous { get; } = new(AuditActorKind.Anonymous, principal: null);

    public static AuditActor OperatorCommand { get; } = new(AuditActorKind.OperatorCommand, principal: null);

    public AuditActorKind Kind { get; }

    public ResourceId? Principal { get; }

    /// <summary>An operator principal. A <c>null</c> ID, or one not of type <c>principal</c>, fails with <c>core.value.invalid</c>.</summary>
    public static AuditActor ForPrincipal(ResourceId principal) =>
        principal is null || !ReferenceEquals(principal.Type, ResourceTypes.Principal)
            ? throw new GatewayException(ErrorCodes.ValueInvalid)
            : new(AuditActorKind.Principal, principal);

    /// <summary>The stored text: <c>anonymous</c>, <c>operator_command</c>, or the principal's ID.</summary>
    public string ToCode() => Kind switch
    {
        AuditActorKind.Anonymous => "anonymous",
        AuditActorKind.OperatorCommand => "operator_command",
        AuditActorKind.Principal => Principal!.Value,
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
