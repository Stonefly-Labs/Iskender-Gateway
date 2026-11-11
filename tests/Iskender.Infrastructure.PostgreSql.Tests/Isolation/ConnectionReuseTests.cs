using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Isolation;

/// <summary>
/// User Story 3 scenario 5 and FR-008: with one pooled connection, a unit in scope B on the connection a unit in scope A
/// used sees none of A's settings, temporary tables, prepared statements, or advisory locks.
/// </summary>
public class ConnectionReuseTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NothingFromScopeAIsVisibleToScopeBOnTheSameConnection(bool commitA)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database, new Dictionary<string, object> { ["poolMinimum"] = 0, ["poolMaximum"] = 1 });

        int pidA;
        await using (var a = await store.OpenAsync(Owners.Project(Owners.OrgA, Owners.ProjectA1)))
        {
            var unit = PostgreSqlUnitOfWork.From(a);
            pidA = await unit.FrameworkCommand("SELECT pg_backend_pid()").ScalarAsync<int>(Token);
            Assert.Equal(Owners.OrgA.Value, await unit.FrameworkCommand("SELECT current_setting('iskender.organization_id')").ScalarAsync<string>(Token));
            await unit.FrameworkCommand("SELECT set_config('iskender_test.leak', 'from-a', false)").ExecuteAsync(Token);
            await unit.FrameworkCommand("CREATE TEMP TABLE leak_a (id int)").ExecuteAsync(Token);
            await unit.FrameworkCommand("PREPARE leak_a_statement AS SELECT 1").ExecuteAsync(Token);
            await unit.FrameworkCommand("SELECT pg_advisory_xact_lock(424242)").ExecuteAsync(Token);
            Assert.Equal(1, await unit.FrameworkCommand("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid()").ScalarAsync<long>(Token));
            if (commitA)
            {
                await a.CommitAsync(Token);
            }
        }

        await using var b = await store.OpenAsync(Owners.Organization(Owners.OrgB));
        var reader = PostgreSqlUnitOfWork.From(b);
        Assert.Equal(pidA, await reader.FrameworkCommand("SELECT pg_backend_pid()").ScalarAsync<int>(Token));

        var seen = Assert.Single(await reader.FrameworkCommand(
                "SELECT current_setting('iskender.scope_kind'), current_setting('iskender.organization_id'), current_setting('iskender.project_id'), " +
                "coalesce(current_setting('iskender_test.leak', true), ''), " +
                "(SELECT count(*) FROM pg_class WHERE relnamespace = pg_my_temp_schema()), " +
                "(SELECT count(*) FROM pg_prepared_statements WHERE name = 'leak_a_statement'), " +
                "(SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid())")
            .QueryAsync(row => (row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetInt64(4), row.GetInt64(5), row.GetInt64(6)), Token));

        Assert.Equal(("organization", Owners.OrgB.Value, "", "", 0L, 0L, 0L), seen);
    }
}
