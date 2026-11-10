using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>FR-022: on a migrated database the runtime login can change no schema, owns nothing, and bypasses nothing.</summary>
public class MigrationIdentityTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Ddl() =>
    [
        "CREATE TABLE isk_alpha.intruder (id int)",
        "CREATE TABLE isk_beta.intruder (id int)",
        "CREATE TABLE isk_migrations.intruder (id int)",
        "CREATE TABLE public.intruder (id int)",
        "CREATE SCHEMA isk_intruder",
        "ALTER TABLE isk_alpha.widgets ADD COLUMN intruder int",
        "ALTER TABLE isk_beta.counters DISABLE ROW LEVEL SECURITY",
        "DROP POLICY widgets_owner ON isk_alpha.widgets",
        "DROP TABLE isk_alpha.settings",
        "DROP INDEX isk_beta.counters_value",
        "CREATE INDEX intruder ON isk_alpha.widgets (name)",
    ];

    [Theory]
    [MemberData(nameof(Ddl))]
    public async Task TheRuntimeLoginCannotCreateAlterOrDrop(string ddl)
    {
        database.RequirePostgreSql();
        await using (var connection = new NpgsqlConnection(database.RuntimeConnectionString()))
        {
            await connection.OpenAsync(Token);
            await using var command = new NpgsqlCommand(ddl, connection);
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Token))).SqlState);
        }

        await using var store = TestStore.For(database);
        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).FrameworkCommand(ddl).ExecuteAsync(Token));

        Assert.Same(ErrorCodes.Internal, failure.Code);
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4014 && logged.Fields.Any(field => field.Key == "SqlStateClass" && (string?)field.Value == "42"));
    }

    [Fact]
    public async Task TheRuntimeRolesOwnNothingAndAreNeitherSuperuserNorBypassRowSecurity()
    {
        database.RequirePostgreSql();
        const string Roles = $"('{PostgreSqlFixture.RuntimeLogin}', '{PostgreSqlFixture.RuntimeGroup}')";

        Assert.Equal(0, await database.Fixture.ScalarAsync<long>(database.Name, $"SELECT count(*) FROM pg_class WHERE relowner IN (SELECT oid FROM pg_roles WHERE rolname IN {Roles})"));
        Assert.Equal(0, await database.Fixture.ScalarAsync<long>(database.Name, $"SELECT count(*) FROM pg_namespace WHERE nspowner IN (SELECT oid FROM pg_roles WHERE rolname IN {Roles})"));
        Assert.Equal(0, await database.Fixture.ScalarAsync<long>(database.Name, $"SELECT count(*) FROM pg_roles WHERE rolname IN {Roles} AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole)"));
        Assert.True(await database.Fixture.ScalarAsync<bool>(database.Name, $"SELECT pg_has_role('{PostgreSqlFixture.RuntimeLogin}', '{PostgreSqlFixture.RuntimeGroup}', 'MEMBER')"));
        Assert.Equal(2, await database.Fixture.ScalarAsync<long>(database.Name, $"SELECT count(*) FROM pg_roles WHERE rolname IN {Roles}"));
    }

    [Fact]
    public async Task TheMigratedTemplateHoldsReleaseOne()
    {
        database.RequirePostgreSql();

        Assert.Equal("alpha/1,alpha/2,alpha/3,beta/1,beta/2,beta/3", await database.Fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(module || '/' || version, ',' ORDER BY module, version) FROM isk_migrations.history"));
        Assert.Equal(PostgreSqlFixture.MigratorLogin, await database.Fixture.ScalarAsync<string>(database.Name, "SELECT DISTINCT applied_by FROM isk_migrations.history"));
    }
}
