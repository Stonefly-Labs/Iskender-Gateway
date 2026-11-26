using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// The PostgreSQL <see cref="IInstallationState"/> (operator-security contract, Installation state, research R3): reads
/// <c>isk_identity.installation</c> in its own operator-scope unit on every call, with no cache. No row is
/// <c>uninitialized</c>. A store that cannot be read throws its persistence failure. Owned by ISK-0010.
/// </summary>
internal sealed class PostgreSqlInstallationState(IUnitOfWorkFactory units) : IInstallationState
{
    private const string ReadSql = "SELECT initialized_at FROM isk_identity.installation";

    private readonly IUnitOfWorkFactory _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);

    public async ValueTask<InstallationStatus> ReadAsync(CancellationToken cancellationToken)
    {
        await using var unit = await IdentityUnits.OpenAsync(_units, "installation.read", cancellationToken);
        var rows = await PostgreSqlUnitOfWork.From(unit).Command(IdentityTables.Installation, ReadSql)
            .QueryAsync(reader => reader.GetFieldValue<DateTimeOffset>(0), cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return rows.Count == 0 ? InstallationStatus.Uninitialized : InstallationStatus.Initialized(rows[0]);
    }
}
