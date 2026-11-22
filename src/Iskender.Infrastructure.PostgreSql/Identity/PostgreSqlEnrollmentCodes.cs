using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Telemetry;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// The PostgreSQL <see cref="IEnrollmentCodes"/> (operator-security contract, Enrollment, research R11), in the caller's
/// operator-scope unit, so a consumer attaches its sign-in method in the same commit. It locks the code's row with
/// <c>FOR UPDATE</c>, as the redemption does, and succeeds only for an unconsumed, unexpired <c>enrollment</c> code bound
/// to the principal whose secret matches in constant time, against the database's clock read in that <c>SELECT</c>. It
/// then marks the code consumed and appends an <c>enrollment.consumed</c> record with the consumer's actor and the
/// principal as target. Anything else throws <c>operator.enrollment_invalid</c> before any write, so it changes nothing.
/// It never commits: the caller does. ISK-0010 ships no caller (FR-011a). Owned by ISK-0010.
/// </summary>
internal sealed class PostgreSqlEnrollmentCodes(IAuditRecorder audit) : IEnrollmentCodes
{
    private const string LockSql =
        "SELECT purpose, secret_hash, principal_id, consumed_at IS NOT NULL, expires_at <= statement_timestamp() "
        + "FROM isk_identity.tokens WHERE token_id = $1 FOR UPDATE";

    private const string ConsumeSql = "UPDATE isk_identity.tokens SET consumed_at = statement_timestamp() WHERE token_id = $1";

    private readonly IAuditRecorder _audit = audit ?? throw new GatewayException(ErrorCodes.ValueInvalid);

    public async ValueTask ConsumeAsync(
        IUnitOfWork unit, ResourceId principal, OperatorTokenText code, OperatorRequestContext context, CancellationToken cancellationToken)
    {
        if (unit is null || unit.Scope != UnitOfWorkScope.Operator || principal is null || code is null || context is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var rows = await PostgreSqlUnitOfWork.From(unit).Command(IdentityTables.Tokens, LockSql)
            .With(code.TokenId)
            .QueryAsync(reader => new LockedCode(reader.GetString(0), reader.GetFieldValue<byte[]>(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4)), cancellationToken);
        if (rows.Count == 0 || !Usable(rows[0], principal, code))
        {
            throw new GatewayException(ErrorCodes.OperatorEnrollmentInvalid);
        }

        await PostgreSqlUnitOfWork.From(unit).Command(IdentityTables.Tokens, ConsumeSql).With(code.TokenId).ExecuteAsync(cancellationToken);
        await _audit.AppendAsync(unit, IdentityAudit.Succeeded(context, AuditAction.EnrollmentConsumed, principal), cancellationToken);
    }

    // The hash first, in constant time, then the purpose, the binding, consumed, and expiry.
    private static bool Usable(LockedCode row, ResourceId principal, OperatorTokenText code) =>
        OperatorTokenHash.Verify(code.Purpose, code, row.Hash)
        && code.Purpose == OperatorTokenPurpose.Enrollment
        && OperatorTokenPurposes.TryParse(row.Purpose, out var purpose) && purpose == OperatorTokenPurpose.Enrollment
        && string.Equals(row.Principal, principal.Value, StringComparison.Ordinal)
        && !row.Consumed
        && !row.Expired;

    private sealed record LockedCode(string Purpose, byte[] Hash, string? Principal, bool Consumed, bool Expired);
}
