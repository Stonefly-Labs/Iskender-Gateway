using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Telemetry;

/// <summary>
/// One operator audit record (operator-security contract, Audit records rule 2). Its scope is always <c>operator</c>.
/// It has no field for a token, hash, network address, request body, or configuration value (FR-027).
/// <see cref="IsValid"/> holds when the reason is <see cref="AuditReason.None"/> exactly when the result is
/// <see cref="AuditResult.Succeeded"/>, every value is defined and set, and a target is a <c>principal</c> ID. Owned by
/// ISK-0010.
/// </summary>
public sealed record AuditRecord(
    AuditActor Actor,
    ResourceId? TargetPrincipal,
    AuditAction Action,
    AuditResult Result,
    AuditReason Reason,
    ConfigurationRevision ConfigurationRevision,
    RequestCorrelationId CorrelationId)
{
    /// <summary>The scope every record of this spec holds.</summary>
    public const string Scope = "operator";

    public bool IsValid =>
        Actor is not null && ConfigurationRevision is not null && CorrelationId is not null
        && (Actor.Kind != AuditActorKind.Principal || Actor.Principal is not null)
        && Enum.IsDefined(Actor.Kind) && Enum.IsDefined(Action) && Enum.IsDefined(Result) && Enum.IsDefined(Reason)
        && (Reason == AuditReason.None) == (Result == AuditResult.Succeeded)
        && (TargetPrincipal is null || ReferenceEquals(TargetPrincipal.Type, ResourceTypes.Principal));

    /// <summary>Returns the record, or fails with <c>core.value.invalid</c> when it is not <see cref="IsValid"/>.</summary>
    public AuditRecord RequireValid() => IsValid ? this : throw new GatewayException(ErrorCodes.ValueInvalid);
}
