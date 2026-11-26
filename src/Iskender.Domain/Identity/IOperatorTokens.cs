namespace Iskender.Domain.Identity;

/// <summary>
/// Issues bootstrap and recovery tokens (operator-security contract, Tokens rule 6). In one operator-scope unit, it
/// serializes with other issuers on a transaction-scoped advisory lock, supersedes every active token of the purpose,
/// stores only the new token's hash, writes the audit records, and commits, and only then returns the token, whose text
/// is revealed once. <see cref="OperatorTokenPurpose.Bootstrap"/> fails with <c>operator.already_initialized</c> on an
/// initialized installation, <see cref="OperatorTokenPurpose.Recovery"/> with <c>operator.not_initialized</c> on one that
/// is not, and <see cref="OperatorTokenPurpose.Enrollment"/> with <c>core.value.invalid</c> before any statement. A store
/// failure is the persistence failure. Owned by ISK-0010.
/// </summary>
public interface IOperatorTokens
{
    ValueTask<IssuedOperatorToken> IssueAsync(OperatorTokenPurpose purpose, OperatorRequestContext context, CancellationToken cancellationToken);
}
