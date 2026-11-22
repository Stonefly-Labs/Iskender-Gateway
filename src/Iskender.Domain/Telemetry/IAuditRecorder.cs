using Iskender.Domain.Ports;

namespace Iskender.Domain.Telemetry;

/// <summary>
/// Appends an audit record in the caller's unit of work, so the record commits or rolls back with the change it records
/// (operator-security contract, Audit records rule 1). It writes only in <paramref name="unit"/>'s transaction, and only
/// in an operator-scope unit, otherwise <c>persistence.scope_not_permitted</c>. An invalid record fails with
/// <c>core.value.invalid</c> before any statement. Owned by ISK-0010.
/// </summary>
public interface IAuditRecorder
{
    ValueTask AppendAsync(IUnitOfWork unit, AuditRecord record, CancellationToken cancellationToken);
}
