using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// The PostgreSQL <see cref="IOperatorAuthorization"/> (operator-security contract, Admin routes rule 9, research R10):
/// reads the principal's rows of <c>isk_identity.operator_grants</c> in its own operator-scope unit on every call, with no
/// cache, maps each role to its actions through <see cref="OperatorRoles"/> in code, and allows only when one role holds
/// the action. A stored role this build does not know grants nothing. A store that cannot be read throws its persistence
/// failure, so the caller fails closed. Owned by ISK-0010.
/// </summary>
internal sealed class PostgreSqlOperatorAuthorization(IUnitOfWorkFactory units) : IOperatorAuthorization
{
    private const string ReadSql = "SELECT role FROM isk_identity.operator_grants WHERE principal_id = $1";

    private readonly IUnitOfWorkFactory _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);

    public async ValueTask<OperatorDecision> DecideAsync(ResourceId principal, OperatorAction action, CancellationToken cancellationToken)
    {
        if (principal is null || action is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (!ReferenceEquals(principal.Type, ResourceTypes.Principal))
        {
            return OperatorDecision.Denied;
        }

        await using var unit = await IdentityUnits.OpenAsync(_units, "authorization.decide", cancellationToken);
        var roles = await PostgreSqlUnitOfWork.From(unit).Command(IdentityTables.OperatorGrants, ReadSql)
            .With(principal.Value)
            .QueryAsync(reader => reader.GetString(0), cancellationToken);
        await unit.CommitAsync(cancellationToken);

        return roles.Any(role => OperatorRoles.TryGet(role, out var known) && known.Holds(action)) ? OperatorDecision.Allowed : OperatorDecision.Denied;
    }
}
