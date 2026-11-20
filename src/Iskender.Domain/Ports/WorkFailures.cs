using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// How a handler declares a permanent failure, which poisons the item at once (work contract, Retry and poison rules 1
/// and 3). The dispatcher classifies any other exception by its code. Owned by ISK-0009.
/// </summary>
public static class WorkFailures
{
    /// <summary>The diagnostics key that marks a permanent failure.</summary>
    public const string FailureKey = "work_failure";

    /// <summary>The diagnostics value that marks a permanent failure.</summary>
    public const string PermanentValue = "permanent";

    /// <summary>
    /// A failure with <paramref name="code"/>, such as <c>work.payload_invalid</c>, whose diagnostics hold
    /// <c>work_failure = permanent</c>. A code that is not registered and active fails as <see cref="GatewayError.Create(ErrorCode, RequestCorrelationId, OutputVisibility, RetryAdvice?, ProviderErrorMetadata?, ErrorDiagnostics?)"/> does.
    /// </summary>
    public static GatewayException Permanent(ErrorCode code) =>
        new(GatewayError.Create(
            code,
            RequestCorrelationId.New(),
            OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(values: [new KeyValuePair<string, string>(FailureKey, PermanentValue)])));

    /// <summary>Whether <paramref name="exception"/> was raised by <see cref="Permanent"/>.</summary>
    public static bool IsPermanent(GatewayException exception) =>
        exception?.Error?.Diagnostics.Values.Any(value => value is { Key: FailureKey, Value: PermanentValue }) == true;
}
