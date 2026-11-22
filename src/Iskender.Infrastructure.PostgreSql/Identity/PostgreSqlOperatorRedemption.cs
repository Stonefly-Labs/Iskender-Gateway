using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Telemetry;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// The PostgreSQL <see cref="IOperatorRedemption"/> (operator-security contract, Redemption, research R5), as one
/// operator-scope unit at Read Committed with no advisory lock. It locks the token's row with <c>FOR UPDATE</c>; with no
/// row it rolls back as <c>unknown</c>, with no audit record. In code it then checks the hash in constant time, the
/// purpose against the route, consumed, superseded, and expiry against the database's clock read in that
/// <c>SELECT</c>, and the first that fails writes its failure record and commits with no token change. A bootstrap then
/// inserts the installation row with <c>ON CONFLICT DO NOTHING</c>, and a recovery checks that it exists, each failing
/// the same way. Last it consumes the token and inserts the principal, its <c>system_operator</c> grant, its enrollment
/// code, and the success record, in the same commit. Concurrent redemptions of one token serialize on the row lock, and
/// two bootstrap tokens on the installation's primary key, so exactly one bootstrap succeeds. A store failure, or a
/// commit whose outcome is unknown, throws and is never retried. Events 9001 and 9002 and the redemption metric are
/// written by the admin pipeline from the outcome, since the adapter does not reference <c>Iskender.Management</c>. Owned
/// by ISK-0010.
/// </summary>
internal sealed class PostgreSqlOperatorRedemption : IOperatorRedemption
{
    private const string LockSql =
        "SELECT purpose, secret_hash, consumed_at IS NOT NULL, superseded_at IS NOT NULL, expires_at <= statement_timestamp() "
        + "FROM isk_identity.tokens WHERE token_id = $1 FOR UPDATE";

    private const string InitializeSql =
        "INSERT INTO isk_identity.installation (state, initialized_at, initialized_by) VALUES ('initialized', statement_timestamp(), $1) "
        + "ON CONFLICT DO NOTHING RETURNING singleton";

    private const string InstalledSql = "SELECT EXISTS (SELECT 1 FROM isk_identity.installation)";

    private const string ConsumeSql = "UPDATE isk_identity.tokens SET consumed_at = statement_timestamp() WHERE token_id = $1";

    private const string PrincipalSql =
        "INSERT INTO isk_identity.principals (principal_id, kind, created_at, created_through) VALUES ($1, 'operator', statement_timestamp(), $2)";

    private const string GrantSql =
        "INSERT INTO isk_identity.operator_grants (principal_id, role, granted_at) VALUES ($1, $2, statement_timestamp())";

    private const string EnrollmentSql =
        "INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, principal_id, issued_at, expires_at) "
        + "VALUES ($1, 'enrollment', $2, $3, statement_timestamp(), statement_timestamp() + $4::interval) RETURNING expires_at";

    private readonly IUnitOfWorkFactory _units;
    private readonly IAuditRecorder _audit;
    private readonly IdentityLifetimes _lifetimes;
    private readonly Func<string, CancellationToken, ValueTask>? _pause;

    /// <summary>
    /// <paramref name="pause"/> is a test seam only: when set, it runs after each named step, so a test can hold a
    /// redemption between its statements. Production code never sets it.
    /// </summary>
    public PostgreSqlOperatorRedemption(
        IUnitOfWorkFactory units, IAuditRecorder audit, IdentityLifetimes lifetimes, Func<string, CancellationToken, ValueTask>? pause = null)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _audit = audit ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _lifetimes = lifetimes ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _pause = pause;
    }

    public async ValueTask<RedemptionOutcome> RedeemAsync(
        OperatorTokenPurpose route, OperatorTokenText token, OperatorRequestContext context, CancellationToken cancellationToken)
    {
        if (route is not (OperatorTokenPurpose.Bootstrap or OperatorTokenPurpose.Recovery) || token is null || context is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var action = route == OperatorTokenPurpose.Bootstrap ? AuditAction.BootstrapRedeemed : AuditAction.RecoveryRedeemed;
        await using var unit = await IdentityUnits.OpenAsync(_units, route == OperatorTokenPurpose.Bootstrap ? "bootstrap.redeem" : "recovery.redeem", cancellationToken);
        var adapter = PostgreSqlUnitOfWork.From(unit);

        var rows = await adapter.Command(IdentityTables.Tokens, LockSql)
            .With(token.TokenId)
            .QueryAsync(reader => new LockedToken(reader.GetString(0), reader.GetFieldValue<byte[]>(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4)), cancellationToken);
        if (rows.Count == 0)
        {
            return RedemptionOutcome.Failure(RedemptionFailureReason.Unknown);
        }

        await PauseAsync("locked", cancellationToken);
        if (Check(rows[0], route, token) is { } refused)
        {
            return await FailAsync(unit, context, action, refused, cancellationToken);
        }

        var principal = ResourceId.New(ResourceTypes.Principal);
        if (route == OperatorTokenPurpose.Bootstrap)
        {
            var inserted = await adapter.Command(IdentityTables.Installation, InitializeSql)
                .With(principal.Value)
                .QueryAsync(reader => reader.GetBoolean(0), cancellationToken);
            if (inserted.Count == 0)
            {
                return await FailAsync(unit, context, action, RedemptionFailureReason.AlreadyInitialized, cancellationToken);
            }
        }
        else if (!await adapter.Command(IdentityTables.Installation, InstalledSql).ScalarAsync<bool>(cancellationToken))
        {
            return await FailAsync(unit, context, action, RedemptionFailureReason.NotInitialized, cancellationToken);
        }

        await PauseAsync("installed", cancellationToken);
        await adapter.Command(IdentityTables.Tokens, ConsumeSql).With(token.TokenId).ExecuteAsync(cancellationToken);
        await adapter.Command(IdentityTables.Principals, PrincipalSql).With(principal.Value).With(route.ToCode()).ExecuteAsync(cancellationToken);
        await adapter.Command(IdentityTables.OperatorGrants, GrantSql).With(principal.Value).With(OperatorRoles.SystemOperator.Name).ExecuteAsync(cancellationToken);

        var code = OperatorTokenText.Generate(OperatorTokenPurpose.Enrollment);
        var expiresAt = await adapter.Command(IdentityTables.Tokens, EnrollmentSql)
            .With(code.TokenId)
            .With(OperatorTokenHash.Compute(OperatorTokenPurpose.Enrollment, code))
            .With(principal.Value)
            .With(_lifetimes.EnrollmentCodeLifetime)
            .ScalarAsync<DateTime>(cancellationToken);
        await _audit.AppendAsync(unit, IdentityAudit.Succeeded(context, action, principal), cancellationToken);
        await PauseAsync("written", cancellationToken);
        await unit.CommitAsync(cancellationToken);

        return RedemptionOutcome.Success(principal, new IssuedOperatorToken(code, new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc))));
    }

    // The checks in the contract's order, after the row is found: secret, purpose, consumed, superseded, expiry.
    private static RedemptionFailureReason? Check(LockedToken row, OperatorTokenPurpose route, OperatorTokenText token)
    {
        if (!OperatorTokenHash.Verify(token.Purpose, token, row.Hash))
        {
            return RedemptionFailureReason.WrongSecret;
        }

        if (!OperatorTokenPurposes.TryParse(row.Purpose, out var purpose) || purpose != route)
        {
            return RedemptionFailureReason.WrongPurpose;
        }

        return row.Consumed ? RedemptionFailureReason.Consumed
            : row.Superseded ? RedemptionFailureReason.Superseded
            : row.Expired ? RedemptionFailureReason.Expired
            : null;
    }

    private async ValueTask<RedemptionOutcome> FailAsync(
        IUnitOfWork unit, OperatorRequestContext context, AuditAction action, RedemptionFailureReason reason, CancellationToken cancellationToken)
    {
        await _audit.AppendAsync(unit, IdentityAudit.Failed(context, action, reason), cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return RedemptionOutcome.Failure(reason);
    }

    private ValueTask PauseAsync(string step, CancellationToken cancellationToken) =>
        _pause is null ? ValueTask.CompletedTask : _pause(step, cancellationToken);

    private sealed record LockedToken(string Purpose, byte[] Hash, bool Consumed, bool Superseded, bool Expired);
}
