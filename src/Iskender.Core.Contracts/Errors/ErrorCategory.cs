using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// One of the 13 error categories. The set is closed: there is no public constructor, and adding or removing a category
/// needs a revision of the published contract (FR-020, FR-026).
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class ErrorCategory
{
    public static readonly ErrorCategory InvalidRequest = new("invalid_request", RetryRule.Never);
    public static readonly ErrorCategory Unauthenticated = new("unauthenticated", RetryRule.Never);
    public static readonly ErrorCategory PermissionDenied = new("permission_denied", RetryRule.Never);
    public static readonly ErrorCategory NotFound = new("not_found", RetryRule.Never);
    public static readonly ErrorCategory Conflict = new("conflict", RetryRule.Never);
    public static readonly ErrorCategory CapabilityUnavailable = new("capability_unavailable", RetryRule.Never);
    public static readonly ErrorCategory LimitExceeded = new("limit_exceeded", RetryRule.WhenResetKnown);
    public static readonly ErrorCategory BudgetBlocked = new("budget_blocked", RetryRule.Never);
    public static readonly ErrorCategory UpstreamUnavailable = new("upstream_unavailable", RetryRule.BeforeOutputOnly);
    public static readonly ErrorCategory UpstreamFailed = new("upstream_failed", RetryRule.BeforeOutputWhenRetriable);
    public static readonly ErrorCategory DeadlineExceeded = new("deadline_exceeded", RetryRule.Never);
    public static readonly ErrorCategory Cancelled = new("cancelled", RetryRule.Never);
    public static readonly ErrorCategory Internal = new("internal", RetryRule.Never);

    private ErrorCategory(string id, RetryRule retryRule)
    {
        Id = id;
        RetryRule = retryRule;
    }

    /// <summary>Every category, in contract order.</summary>
    public static IReadOnlyList<ErrorCategory> All { get; } = Array.AsReadOnly(
    [
        InvalidRequest, Unauthenticated, PermissionDenied, NotFound, Conflict, CapabilityUnavailable, LimitExceeded,
        BudgetBlocked, UpstreamUnavailable, UpstreamFailed, DeadlineExceeded, Cancelled, Internal,
    ]);

    /// <summary>The category ID, such as <c>invalid_request</c>.</summary>
    public string Id { get; }

    /// <summary>When retry advice is permitted.</summary>
    public RetryRule RetryRule { get; }

    public override string ToString() => Id;
}
