using Iskender.Identity.Tests.Fixture;
using Npgsql;

namespace Iskender.Identity.Tests.Audit;

/// <summary>
/// ISK-0010 T042, US5.3, FR-002, FR-029, operator-security contract Audit records rule 3: as <c>iskender_runtime</c>,
/// <c>UPDATE</c>, <c>DELETE</c>, and <c>TRUNCATE</c> on <c>isk_audit.records</c> and on <c>isk_identity.installation</c>
/// are each refused by the database with <c>insufficient_privilege</c>.
/// </summary>
public class AuditPrivilegeTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (operators.Postgres.UnavailableReason is null)
        {
            await operators.ResetAsync();
            await operators.ExecuteAsync(
                "INSERT INTO isk_audit.records (audit_id, actor, action, scope, result, reason, configuration_revision, correlation_id) "
                + "VALUES ('audit_fixture', 'operator_command', 'bootstrap_token.issued', 'operator', 'succeeded', 'none', 'test-rev', 'fixture')");
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public static TheoryData<string> Statements() =>
    [
        "UPDATE isk_audit.records SET reason = 'none'",
        "DELETE FROM isk_audit.records",
        "TRUNCATE isk_audit.records",
        "UPDATE isk_identity.installation SET initialized_by = initialized_by",
        "DELETE FROM isk_identity.installation",
        "TRUNCATE isk_identity.installation",
    ];

    [Theory]
    [MemberData(nameof(Statements))]
    public async Task TheRuntimeLoginIsRefused(string statement)
    {
        operators.RequirePostgreSql();
        await using var connection = await operators.Sources.Runtime.OpenConnectionAsync(Token);
        await using var command = new NpgsqlCommand(statement, connection);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
        Assert.Equal(1L, await operators.AuditCountAsync());
    }
}
