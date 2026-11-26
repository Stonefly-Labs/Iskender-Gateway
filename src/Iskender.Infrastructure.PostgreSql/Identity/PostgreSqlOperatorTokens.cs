using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Telemetry;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// The PostgreSQL <see cref="IOperatorTokens"/> (operator-security contract, Tokens rules 3, 4, and 6, research R6). In
/// one operator-scope unit it takes the transaction-scoped advisory lock <c>(operator, identity, token_issue)</c> with a
/// 5-second bound, so concurrent issuers serialize rather than one failing on the partial unique index; marks every active
/// token of the purpose superseded by the database's clock, waiting on any redemption's row lock; only then reads the
/// installation state, so a redemption that committed meanwhile refuses the issue; writes one <c>token.superseded</c>
/// record each; inserts the new token's hash with its expiry measured from
/// <c>statement_timestamp()</c>; writes the issue record; and commits. Only then is the token returned. Owned by ISK-0010.
/// </summary>
internal sealed class PostgreSqlOperatorTokens : IOperatorTokens
{
    /// <summary>The issuers' mutual exclusion, in the operator scope.</summary>
    public static AdvisoryLockName IssueLock => new(IdentityMigrations.Name, "token_issue");

    /// <summary>How long an issuer waits for another to commit before failing with <c>persistence.lock_timeout</c>.</summary>
    public static readonly TimeSpan IssueLockBound = TimeSpan.FromSeconds(5);

    private const string InstalledSql = "SELECT EXISTS (SELECT 1 FROM isk_identity.installation)";

    private const string SupersedeSql =
        "UPDATE isk_identity.tokens SET superseded_at = statement_timestamp() "
        + "WHERE purpose = $1 AND consumed_at IS NULL AND superseded_at IS NULL RETURNING token_id";

    private const string InsertSql =
        "INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) "
        + "VALUES ($1, $2, $3, statement_timestamp(), statement_timestamp() + $4::interval) RETURNING expires_at";

    private readonly IUnitOfWorkFactory _units;
    private readonly IAuditRecorder _audit;
    private readonly IdentityLifetimes _lifetimes;

    public PostgreSqlOperatorTokens(IUnitOfWorkFactory units, IAuditRecorder audit, IdentityLifetimes lifetimes)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _audit = audit ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _lifetimes = lifetimes ?? throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public async ValueTask<IssuedOperatorToken> IssueAsync(OperatorTokenPurpose purpose, OperatorRequestContext context, CancellationToken cancellationToken)
    {
        if (purpose is not (OperatorTokenPurpose.Bootstrap or OperatorTokenPurpose.Recovery) || context is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var token = OperatorTokenText.Generate(purpose);
        await using var unit = await IdentityUnits.OpenAsync(_units, "token.issue", cancellationToken);
        var adapter = PostgreSqlUnitOfWork.From(unit);
        await unit.AcquireAdvisoryLockAsync(IssueLock, AdvisoryLockWait.Bounded(IssueLockBound), cancellationToken);

        // The supersede waits on any redemption's row lock. Under Read Committed a state read before it is stale once
        // that lock clears, so the state is read after it, in a statement that sees the redemption's commit (CONV-0010-001).
        var superseded = await adapter.Command(IdentityTables.Tokens, SupersedeSql)
            .With(purpose.ToCode())
            .QueryAsync(reader => reader.GetString(0), cancellationToken);

        var installed = await adapter.Command(IdentityTables.Installation, InstalledSql).ScalarAsync<bool>(cancellationToken);
        if (purpose == OperatorTokenPurpose.Bootstrap && installed)
        {
            throw new GatewayException(ErrorCodes.OperatorAlreadyInitialized);
        }

        if (purpose == OperatorTokenPurpose.Recovery && !installed)
        {
            throw new GatewayException(ErrorCodes.OperatorNotInitialized);
        }

        foreach (var _ in superseded)
        {
            await _audit.AppendAsync(unit, IdentityAudit.Succeeded(context, AuditAction.TokenSuperseded), cancellationToken);
        }

        var expiresAt = await adapter.Command(IdentityTables.Tokens, InsertSql)
            .With(token.TokenId)
            .With(purpose.ToCode())
            .With(OperatorTokenHash.Compute(purpose, token))
            .With(_lifetimes.TokenLifetime)
            .ScalarAsync<DateTime>(cancellationToken);
        var action = purpose == OperatorTokenPurpose.Bootstrap ? AuditAction.BootstrapTokenIssued : AuditAction.RecoveryTokenIssued;
        await _audit.AppendAsync(unit, IdentityAudit.Succeeded(context, action), cancellationToken);
        await unit.CommitAsync(cancellationToken);

        return new IssuedOperatorToken(token, new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)), superseded.Count);
    }
}
