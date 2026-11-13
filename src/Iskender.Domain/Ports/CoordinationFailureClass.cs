using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The fixed classes a coordination failure falls into (TR-12, contract Failures rule 2). Contention and a denied
/// reservation are results, not failure classes. Owned by ISK-0005.
/// </summary>
public enum CoordinationFailureClass
{
    Unavailable,
    Timeout,
    Cancelled,
    LeaseLost,
    FencingRejected,
    CapabilityUnsupported,
    ScopeViolation,
    AuthenticationFailed,
    Unexpected,
}

/// <summary>
/// Builds and reads coordination failures: a <see cref="GatewayException"/> carrying only a registered code and its safe
/// message, with the <c>ErrorDiagnostics</c> values <c>failure_class</c> and, for a write whose outcome is unknown,
/// <c>outcome = unknown</c> (contract Failures rule 1). Never an inner exception, key, host, or server text. Owned by ISK-0005.
/// </summary>
public static class CoordinationFailures
{
    /// <summary>The <c>ErrorDiagnostics</c> key holding the failure class.</summary>
    public const string FailureClassKey = "failure_class";

    /// <summary>The <c>ErrorDiagnostics</c> key holding <c>unknown</c> for a write whose outcome is unknown.</summary>
    public const string OutcomeKey = "outcome";

    public static string ToCode(this CoordinationFailureClass failureClass) => failureClass switch
    {
        CoordinationFailureClass.Unavailable => "unavailable",
        CoordinationFailureClass.Timeout => "timeout",
        CoordinationFailureClass.Cancelled => "cancelled",
        CoordinationFailureClass.LeaseLost => "lease_lost",
        CoordinationFailureClass.FencingRejected => "fencing_rejected",
        CoordinationFailureClass.CapabilityUnsupported => "capability_unsupported",
        CoordinationFailureClass.ScopeViolation => "scope_violation",
        CoordinationFailureClass.AuthenticationFailed => "authentication_failed",
        CoordinationFailureClass.Unexpected => "unexpected",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>A failure with <paramref name="code"/>, its class, and, when <paramref name="outcomeUnknown"/>, <c>outcome = unknown</c>.</summary>
    public static GatewayException Raise(ErrorCode code, CoordinationFailureClass failureClass, bool outcomeUnknown = false)
    {
        List<KeyValuePair<string, string>> values = [new(FailureClassKey, failureClass.ToCode())];
        if (outcomeUnknown)
        {
            values.Add(new(OutcomeKey, "unknown"));
        }

        return new GatewayException(GatewayError.Create(code, RequestCorrelationId.New(), OutputVisibility.None, diagnostics: new ErrorDiagnostics(values: values)));
    }

    /// <summary>The failure class a coordination failure carries, or <c>null</c> for any other exception.</summary>
    public static CoordinationFailureClass? ClassOf(GatewayException failure)
    {
        var code = Value(failure, FailureClassKey);
        return code is null ? null : Enum.GetValues<CoordinationFailureClass>().Where(candidate => candidate.ToCode() == code).Cast<CoordinationFailureClass?>().FirstOrDefault();
    }

    /// <summary>Whether a coordination failure marks a write whose outcome is unknown.</summary>
    public static bool IsOutcomeUnknown(GatewayException failure) => Value(failure, OutcomeKey) == "unknown";

    private static string? Value(GatewayException failure, string key) =>
        failure?.Error?.Diagnostics.Values.Where(pair => pair.Key == key).Select(pair => pair.Value).FirstOrDefault();
}
