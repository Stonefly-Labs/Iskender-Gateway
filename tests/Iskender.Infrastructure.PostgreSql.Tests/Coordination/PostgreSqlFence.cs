using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Scoped;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// The PostgreSQL fencing guard in front of a protected row in <c>isk_scoped.slots</c>, written in the same unit as the
/// fence. Each committed write adds a row named for the resource, the server time after the fence, and the token, so
/// reading them in name order gives commit order: the fence's row lock holds each writer until the one before it ends.
/// </summary>
public sealed class PostgreSqlFence(PostgreSqlCoordinatorUnderTest coordinator) : IFenceUnderTest
{
    private const string WriteSql =
        "INSERT INTO isk_scoped.slots (scope_key, name, value) VALUES ($1, $2 || '#' || to_char(clock_timestamp(), 'YYYYMMDDHH24MISSUS') || '#' || $3, $3)";

    private const string ReadSql =
        "SELECT value FROM isk_scoped.slots WHERE {scope} AND left(name, length($2) + 1) = $2 || '#' ORDER BY name";

    public async ValueTask<bool> WriteAsync(CoordinationName resource, Lease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(lease);
        await using var unit = await coordinator.Units(0).OpenAsync(lease.Scope.UnitScope, Options("fence.write"), cancellationToken);
        try
        {
            await coordinator.Guard.AcceptAsync(unit, resource, lease, cancellationToken);
        }
        catch (GatewayException failure) when (ReferenceEquals(failure.Code, ErrorCodes.CoordinationFencingRejected))
        {
            return false;
        }

        await PostgreSqlUnitOfWork.From(unit).Command(ScopedTables.Slots, WriteSql)
            .With(Protected(resource)).With(lease.Token.Value)
            .ExecuteAsync(cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<IReadOnlyList<long>> CommittedTokensAsync(CoordinationScope scope, CoordinationName resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(resource);
        await using var unit = await coordinator.Units(0).OpenAsync(scope.UnitScope, Options("fence.read"), cancellationToken);
        return await PostgreSqlUnitOfWork.From(unit).Command(ScopedTables.Slots, ReadSql)
            .With(Protected(resource))
            .QueryAsync(reader => reader.GetInt64(0), cancellationToken);
    }

    /// <summary>The protected row's name prefix for <paramref name="resource"/>.</summary>
    public static string Protected(CoordinationName resource) => resource.Module + "/" + resource.Name;

    private static UnitOfWorkOptions Options(string operation) => new("scoped", operation);
}
