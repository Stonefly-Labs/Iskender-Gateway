using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Telemetry;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// Builds the identity stores' audit records from an <see cref="OperatorRequestContext"/>, mapping the Identity module's
/// actor and reason types to the Telemetry module's, which rule A17 keeps apart. Owned by ISK-0010.
/// </summary>
internal static class IdentityAudit
{
    public static AuditActor Actor(OperatorActor actor) => actor.Kind switch
    {
        OperatorActorKind.Anonymous => AuditActor.Anonymous,
        OperatorActorKind.OperatorCommand => AuditActor.OperatorCommand,
        _ => AuditActor.ForPrincipal(actor.Principal!),
    };

    public static AuditReason Reason(RedemptionFailureReason reason) => reason switch
    {
        RedemptionFailureReason.WrongSecret => AuditReason.WrongSecret,
        RedemptionFailureReason.WrongPurpose => AuditReason.WrongPurpose,
        RedemptionFailureReason.Consumed => AuditReason.Consumed,
        RedemptionFailureReason.Superseded => AuditReason.Superseded,
        RedemptionFailureReason.Expired => AuditReason.Expired,
        RedemptionFailureReason.AlreadyInitialized => AuditReason.AlreadyInitialized,
        RedemptionFailureReason.NotInitialized => AuditReason.NotInitialized,
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>A succeeded record of <paramref name="action"/> in <paramref name="context"/>.</summary>
    public static AuditRecord Succeeded(OperatorRequestContext context, AuditAction action, ResourceId? target = null) =>
        new(Actor(context.Actor), target, action, AuditResult.Succeeded, AuditReason.None, context.ConfigurationRevision, context.CorrelationId);

    /// <summary>A failed record of <paramref name="action"/> in <paramref name="context"/>, naming <paramref name="reason"/>.</summary>
    public static AuditRecord Failed(OperatorRequestContext context, AuditAction action, RedemptionFailureReason reason) =>
        new(Actor(context.Actor), null, action, AuditResult.Failed, Reason(reason), context.ConfigurationRevision, context.CorrelationId);
}
