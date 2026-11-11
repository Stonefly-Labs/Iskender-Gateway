using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The coordination probe's 7 fixed reasons (TR-14). Owned by ISK-0005.</summary>
public enum CoordinationHealthReason
{
    Ok,
    NotConfigured,
    Unreachable,
    AuthenticationFailed,
    Timeout,
    ReadOnly,
    Unexpected,
}

/// <summary>The snake-case code of each reason, safe to show on a dashboard. Owned by ISK-0005.</summary>
public static class CoordinationHealthReasonCodes
{
    public static string ToCode(this CoordinationHealthReason reason) => reason switch
    {
        CoordinationHealthReason.Ok => "ok",
        CoordinationHealthReason.NotConfigured => "not_configured",
        CoordinationHealthReason.Unreachable => "unreachable",
        CoordinationHealthReason.AuthenticationFailed => "authentication_failed",
        CoordinationHealthReason.Timeout => "timeout",
        CoordinationHealthReason.ReadOnly => "read_only",
        CoordinationHealthReason.Unexpected => "unexpected",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
