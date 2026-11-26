using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Telemetry;

/// <summary>An audited action (operator-security contract, Audit records rule 4). Owned by ISK-0010.</summary>
public enum AuditAction
{
    /// <summary>Code <c>bootstrap_token.issued</c>.</summary>
    BootstrapTokenIssued,

    /// <summary>Code <c>recovery_token.issued</c>.</summary>
    RecoveryTokenIssued,

    /// <summary>Code <c>token.superseded</c>: once per token an issue supersedes.</summary>
    TokenSuperseded,

    /// <summary>Code <c>bootstrap.redeemed</c>.</summary>
    BootstrapRedeemed,

    /// <summary>Code <c>recovery.redeemed</c>.</summary>
    RecoveryRedeemed,

    /// <summary>Code <c>enrollment.consumed</c>.</summary>
    EnrollmentConsumed,
}

/// <summary>An audit record's result. Owned by ISK-0010.</summary>
public enum AuditResult
{
    /// <summary>Code <c>succeeded</c>.</summary>
    Succeeded,

    /// <summary>Code <c>failed</c>.</summary>
    Failed,
}

/// <summary>The stored codes of <see cref="AuditAction"/> and <see cref="AuditResult"/>. Owned by ISK-0010.</summary>
public static class AuditCodes
{
    public static string ToCode(this AuditAction action) => action switch
    {
        AuditAction.BootstrapTokenIssued => "bootstrap_token.issued",
        AuditAction.RecoveryTokenIssued => "recovery_token.issued",
        AuditAction.TokenSuperseded => "token.superseded",
        AuditAction.BootstrapRedeemed => "bootstrap.redeemed",
        AuditAction.RecoveryRedeemed => "recovery.redeemed",
        AuditAction.EnrollmentConsumed => "enrollment.consumed",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    public static string ToCode(this AuditResult result) => result switch
    {
        AuditResult.Succeeded => "succeeded",
        AuditResult.Failed => "failed",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    public static string ToCode(this AuditReason reason) => reason switch
    {
        AuditReason.None => "none",
        AuditReason.WrongSecret => "wrong_secret",
        AuditReason.WrongPurpose => "wrong_purpose",
        AuditReason.Consumed => "consumed",
        AuditReason.Superseded => "superseded",
        AuditReason.Expired => "expired",
        AuditReason.AlreadyInitialized => "already_initialized",
        AuditReason.NotInitialized => "not_initialized",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
