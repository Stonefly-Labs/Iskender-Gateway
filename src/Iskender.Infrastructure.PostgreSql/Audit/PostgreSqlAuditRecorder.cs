using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Domain.Telemetry;

namespace Iskender.Infrastructure.PostgreSql.Audit;

/// <summary>
/// The PostgreSQL <see cref="IAuditRecorder"/> (operator-security contract, Audit records, research R12): inserts one row
/// of <c>isk_audit.records</c> in the caller's operator-scope unit, so the record commits or rolls back with the change it
/// records. An invalid record fails with <c>core.value.invalid</c> before any statement, and a tenant-scope unit fails with
/// <c>persistence.scope_not_permitted</c> through the table's operator kind. Owned by ISK-0010.
/// </summary>
internal sealed class PostgreSqlAuditRecorder : IAuditRecorder
{
    private const string InsertSql =
        "INSERT INTO isk_audit.records (audit_id, actor, target_principal, action, scope, result, reason, configuration_revision, correlation_id) "
        + "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)";

    public async ValueTask AppendAsync(IUnitOfWork unit, AuditRecord record, CancellationToken cancellationToken)
    {
        if (unit is null || record is null || !record.IsValid)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var command = PostgreSqlUnitOfWork.From(unit).Command(AuditTables.Records, InsertSql)
            .With(ResourceId.New(ResourceTypes.AuditEntry).Value)
            .With(record.Actor.ToCode())
            .With(record.TargetPrincipal?.Value)
            .With(record.Action.ToCode())
            .With(AuditRecord.Scope)
            .With(record.Result.ToCode())
            .With(record.Reason.ToCode())
            .With(record.ConfigurationRevision.Value)
            .With(record.CorrelationId.Value);

        await command.ExecuteAsync(cancellationToken);
    }
}
