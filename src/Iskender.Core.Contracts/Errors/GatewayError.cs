using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// A gateway error: a registered code with its category and fixed safe message, the request's correlation ID, retry
/// advice only where permitted, provider metadata, and diagnostics held apart from everything a client sees (FR-022).
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class GatewayError
{
    private static readonly ErrorDiagnostics NoDiagnostics = new();

    private GatewayError(
        ErrorCode code,
        RequestCorrelationId correlationId,
        RetryAdvice? retry,
        ProviderErrorMetadata? provider,
        RedactionStatus redaction,
        ErrorDiagnostics diagnostics)
    {
        Code = code;
        CorrelationId = correlationId;
        Retry = retry;
        Provider = provider;
        Redaction = redaction;
        Diagnostics = diagnostics;
    }

    /// <summary>The registered code.</summary>
    public ErrorCode Code { get; }

    /// <summary>The code's category.</summary>
    public ErrorCategory Category => Code.Category;

    /// <summary>The code's registered safe message. A caller cannot supply other text.</summary>
    public string SafeMessage => Code.SafeMessage;

    /// <summary>The gateway request ID.</summary>
    public RequestCorrelationId CorrelationId { get; }

    /// <summary>Retry advice, present only where the category and output visibility permit.</summary>
    public RetryAdvice? Retry { get; }

    /// <summary>Provider metadata, present only for a provider-originated error.</summary>
    public ProviderErrorMetadata? Provider { get; }

    /// <summary>Whether detail was withheld from the client-visible part.</summary>
    public RedactionStatus Redaction { get; }

    /// <summary>Internal detail. Never serialized to a client, and rendered as <c>[withheld]</c>.</summary>
    public ErrorDiagnostics Diagnostics { get; }

    /// <summary>
    /// Builds an error. Fails with <c>core.error.unregistered_code</c> for a code that is not the registry's instance,
    /// <c>core.error.retired_code</c> for a retired code, and <c>core.error.retry_not_permitted</c> for advice that the
    /// category's retry rule or the output visibility forbids (FR-024, FR-025).
    /// </summary>
    public static GatewayError Create(
        ErrorCode code,
        RequestCorrelationId correlation,
        OutputVisibility visibility,
        RetryAdvice? retry = null,
        ProviderErrorMetadata? provider = null,
        ErrorDiagnostics? diagnostics = null) =>
        Create(ErrorCodes.All, code, correlation, visibility, retry, provider, diagnostics);

    /// <summary>Test seam: builds against a supplied registry, so a test can reach a retired code and every category.</summary>
    internal static GatewayError Create(
        IReadOnlyList<ErrorCode> registry,
        ErrorCode code,
        RequestCorrelationId correlation,
        OutputVisibility visibility,
        RetryAdvice? retry = null,
        ProviderErrorMetadata? provider = null,
        ErrorDiagnostics? diagnostics = null)
    {
        if (code is null || !registry.Any(registered => ReferenceEquals(registered, code)))
        {
            throw new GatewayException(ErrorCodes.ErrorUnregisteredCode);
        }

        if (code.Status == RegistryStatus.Retired)
        {
            throw new GatewayException(ErrorCodes.ErrorRetiredCode);
        }

        if (correlation is null || !Enum.IsDefined(visibility))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (retry is not null && !RetryPermitted(code.Category.RetryRule, visibility, retry))
        {
            throw new GatewayException(ErrorCodes.ErrorRetryNotPermitted);
        }

        diagnostics ??= NoDiagnostics;

        // Every provider field is shown in the envelope, so only diagnostics and the not_found category withhold (TR-3).
        var redaction = !diagnostics.IsEmpty || ReferenceEquals(code.Category, ErrorCategory.NotFound)
            ? RedactionStatus.Withheld
            : RedactionStatus.None;

        return new GatewayError(code, correlation, retry, provider, redaction, diagnostics);
    }

    private static bool RetryPermitted(RetryRule rule, OutputVisibility visibility, RetryAdvice retry) =>
        visibility == OutputVisibility.None && rule switch
        {
            RetryRule.WhenResetKnown => retry.RetryAfter is not null,
            RetryRule.BeforeOutputOnly => true,
            RetryRule.BeforeOutputWhenRetriable => retry.Retriable,
            _ => false,
        };
}
