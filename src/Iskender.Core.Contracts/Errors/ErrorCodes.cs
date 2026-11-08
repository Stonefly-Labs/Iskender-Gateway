using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// The error code registry: the only place an <see cref="ErrorCode"/> is declared (rule H5). The published contract's
/// Error codes table must match it row for row (rule H4).
/// </summary>
[ContractVersion("core-contracts", 1)]
public static partial class ErrorCodes
{
    private const string InternalMessage = "An internal error occurred.";

    public static readonly ErrorCode ResourceIdMalformed = Active(
        "core.resource_id.malformed", ErrorCategory.InvalidRequest,
        "The text is empty, too long, or not `<prefix>_<body>`",
        "The identifier is not in a valid format.");

    public static readonly ErrorCode ResourceIdUnknownType = Active(
        "core.resource_id.unknown_type", ErrorCategory.InvalidRequest,
        "The prefix is not registered",
        "The identifier is not of a known resource type.");

    public static readonly ErrorCode ResourceIdRetiredType = Active(
        "core.resource_id.retired_type", ErrorCategory.InvalidRequest,
        "The prefix is retired",
        "The identifier is of a resource type that is no longer accepted.");

    public static readonly ErrorCode ResourceIdWrongType = Active(
        "core.resource_id.wrong_type", ErrorCategory.InvalidRequest,
        "The ID is a valid ID of another type",
        "The identifier is not of the expected resource type.");

    public static readonly ErrorCode ResourceIdNonCanonical = Active(
        "core.resource_id.non_canonical", ErrorCategory.InvalidRequest,
        "The body has upper case, a character outside the alphabet, or set pad bits",
        "The identifier is not in canonical form.");

    public static readonly ErrorCode ResourceNotFound = Active(
        "core.resource.not_found", ErrorCategory.NotFound,
        "Missing, or not visible to the caller",
        "The resource was not found.");

    public static readonly ErrorCode ContextAlreadyResolved = Active(
        "core.context.already_resolved", ErrorCategory.Internal,
        "A set-once context value was resolved twice",
        InternalMessage);

    public static readonly ErrorCode ContextNotResolved = Active(
        "core.context.not_resolved", ErrorCategory.Internal,
        "A context value was read before it was resolved",
        InternalMessage);

    public static readonly ErrorCode ErrorUnregisteredCode = Active(
        "core.error.unregistered_code", ErrorCategory.Internal,
        "An error was built with a code not in the registry",
        InternalMessage);

    public static readonly ErrorCode ErrorRetiredCode = Active(
        "core.error.retired_code", ErrorCategory.Internal,
        "An error was built with a retired code",
        InternalMessage);

    public static readonly ErrorCode ErrorRetryNotPermitted = Active(
        "core.error.retry_not_permitted", ErrorCategory.Internal,
        "Retry advice was given where the category or visibility forbids it",
        InternalMessage);

    public static readonly ErrorCode RandomSourceFailed = Active(
        "core.random_source_failed", ErrorCategory.Internal,
        "The secure random source failed",
        InternalMessage);

    public static readonly ErrorCode ValueInvalid = Active(
        "core.value.invalid", ErrorCategory.InvalidRequest,
        "A bounded value, such as a revision or provider ID, is outside its limits",
        "A value in the request is outside its allowed limits.");

    public static readonly ErrorCode DeadlineExceeded = Active(
        "core.deadline_exceeded", ErrorCategory.DeadlineExceeded,
        "The request's deadline passed",
        "The request did not complete before its deadline.");

    public static readonly ErrorCode Cancelled = Active(
        "core.cancelled", ErrorCategory.Cancelled,
        "The request was cancelled",
        "The request was cancelled.");

    public static readonly ErrorCode Internal = Active(
        "core.internal", ErrorCategory.Internal,
        "An unexpected gateway fault",
        InternalMessage);

    public static readonly ErrorCode IdentityUnauthenticated = Active(
        "identity.unauthenticated", ErrorCategory.Unauthenticated,
        "No valid credential was presented",
        "No valid credential was presented.");

    public static readonly ErrorCode IdentityAuthDenied = Active(
        "identity.auth_denied", ErrorCategory.PermissionDenied,
        "The caller may not perform the action (`docs/08` reason)",
        "The caller is not allowed to perform this action.");

    public static readonly ErrorCode PolicyModelDisallowed = Active(
        "policy.model_disallowed", ErrorCategory.PermissionDenied,
        "Policy does not allow the requested model (`docs/08` reason)",
        "The requested model is not allowed.");

    public static readonly ErrorCode GovernanceQuotaExceeded = Active(
        "governance.quota_exceeded", ErrorCategory.LimitExceeded,
        "A quota was reached (`docs/08` reason)",
        "A usage limit was reached.");

    public static readonly ErrorCode GovernanceBudgetBlocked = Active(
        "governance.budget_blocked", ErrorCategory.BudgetBlocked,
        "A hard budget blocks the request (`docs/08` reason)",
        "A budget limit blocks this request.");

    public static readonly ErrorCode RoutingNoHealthyBackend = Active(
        "routing.no_healthy_backend", ErrorCategory.UpstreamUnavailable,
        "No healthy backend could take the request (`docs/08` reason)",
        "No backend is currently available to handle the request.");

    /// <summary>Every registered code, in contract order.</summary>
    public static IReadOnlyList<ErrorCode> All { get; } = Validate(
    [
        ResourceIdMalformed, ResourceIdUnknownType, ResourceIdRetiredType, ResourceIdWrongType, ResourceIdNonCanonical,
        ResourceNotFound, ContextAlreadyResolved, ContextNotResolved, ErrorUnregisteredCode, ErrorRetiredCode,
        ErrorRetryNotPermitted, RandomSourceFailed, ValueInvalid, DeadlineExceeded, Cancelled, Internal,
        IdentityUnauthenticated, IdentityAuthDenied, PolicyModelDisallowed, GovernanceQuotaExceeded, GovernanceBudgetBlocked,
        RoutingNoHealthyBackend,
    ]);

    /// <summary>Finds a registered code by its ID, compared ordinally.</summary>
    public static bool TryGet(string id, out ErrorCode? code)
    {
        code = All.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return code is not null;
    }

    /// <summary>Rejects a duplicate ID, an ID outside the code pattern, or a category outside the closed set.</summary>
    internal static IReadOnlyList<ErrorCode> Validate(ErrorCode[] codes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            if (!CodePattern().IsMatch(code.Id))
            {
                throw new InvalidOperationException($"Error code '{code.Id}' does not match {CodePattern()}.");
            }

            if (!seen.Add(code.Id))
            {
                throw new InvalidOperationException($"Error code '{code.Id}' is registered twice.");
            }

            if (!ErrorCategory.All.Contains(code.Category))
            {
                throw new InvalidOperationException($"Error code '{code.Id}' has a category outside the closed set.");
            }
        }

        return Array.AsReadOnly(codes);
    }

    private static ErrorCode Active(string id, ErrorCategory category, string meaning, string safeMessage) =>
        new(id, category, meaning, safeMessage, RegistryStatus.Active, since: 1);

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
