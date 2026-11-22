using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>
/// The <c>identity</c> migration module (ISK-0010 T009, data model "Database objects", research R3, R4, and R11): it is
/// <c>identity</c> 1 to 1 depending on <c>audit</c>; it applies and reverses; each constraint and both partial unique
/// indexes reject a violating row; and the runtime login cannot update, delete, or truncate the installation row, so the
/// state never moves back (FR-001, FR-002).
/// </summary>
public class IdentityMigrationTests(PostgreSqlFixture fixture)
{
    private const string Hash = "decode(repeat('ab', 32), 'hex')";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static MigrationSet Set => new([DirectoryMigrations.Module, WorkMigrations.Module, AuditMigrations.Module, IdentityMigrations.Module]);

    private static string Id(char c) => new(c, 26);

    [Fact]
    public void TheModuleIsIdentityOneToOneDependingOnAudit()
    {
        var module = IdentityMigrations.Module;

        Assert.Equal(("identity", "isk_identity", new SchemaVersionRange(1, 1)), (module.Name, module.Schema, module.SupportedRange));
        Assert.Equal(["audit"], module.DependsOn);
        Assert.Equal(["directory/1", "work/1", "audit/1", "identity/1"], Set.Migrations.Select(script => script.Identity.ToString()));
        Assert.True(Set.Migrations[3].DataLosing);
    }

    [Fact]
    public async Task ItAppliesWithFourOperatorTablesAndReversesWithConfirmation()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(Set);

        await runner.ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal(
            "installation,operator_grants,principals,tokens",
            await fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(c.relname, ',' ORDER BY c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'isk_identity' AND c.relkind = 'r' AND NOT c.relrowsecurity"));
        Assert.Equal(2L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'isk_identity' AND indexname IN ('tokens_active_purpose', 'tokens_active_enrollment') AND indexdef LIKE 'CREATE UNIQUE%'"));

        var unconfirmed = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ReverseAsync(new MigrationReverseRequest(), Token));
        Assert.Same(ErrorCodes.PersistenceReverseRequiresConfirmation, unconfirmed.Code);
        Assert.Equal([new MigrationIdentity("identity", 1)], (await runner.ReverseAsync(new MigrationReverseRequest { ConfirmDataLoss = true }, Token)).Reversed);
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regclass('isk_identity.tokens') IS NULL AND to_regclass('isk_identity.installation') IS NULL AND to_regclass('isk_audit.records') IS NOT NULL"));
    }

    public static TheoryData<string, string> Violating => new()
    {
        { "a second installation row", "INSERT INTO isk_identity.installation (singleton, state, initialized_at, initialized_by) VALUES (true, 'initialized', now(), 'prin_b')" },
        { "a false singleton", "INSERT INTO isk_identity.installation (singleton, state, initialized_at, initialized_by) VALUES (false, 'initialized', now(), 'prin_b')" },
        { "another installation state", "INSERT INTO isk_identity.installation (singleton, state, initialized_at, initialized_by) VALUES (true, 'uninitialized', now(), 'prin_b')" },
        { "a tenant principal", "INSERT INTO isk_identity.principals VALUES ('prin_b', 'tenant', now(), 'bootstrap')" },
        { "another creation path", "INSERT INTO isk_identity.principals VALUES ('prin_b', 'operator', now(), 'signup')" },
        { "another role", "INSERT INTO isk_identity.operator_grants VALUES ('prin_a', 'tenant_admin', now())" },
        { "a grant to no principal", "INSERT INTO isk_identity.operator_grants VALUES ('prin_z', 'system_operator', now())" },
        { "a duplicate grant", "INSERT INTO isk_identity.operator_grants VALUES ('prin_a', 'system_operator', now())" },
        { "a short token ID", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{new string('c', 25)}', 'recovery', {Hash}, now(), now() + interval '1 hour')" },
        { "another purpose", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{Id('c')}', 'session', {Hash}, now(), now() + interval '1 hour')" },
        { "a short hash", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{Id('c')}', 'recovery', decode(repeat('ab', 31), 'hex'), now(), now() + interval '1 hour')" },
        { "an enrollment code with no principal", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{Id('c')}', 'enrollment', {Hash}, now(), now() + interval '1 hour')" },
        { "a bootstrap token with a principal", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, principal_id, issued_at, expires_at) VALUES ('{Id('c')}', 'recovery', {Hash}, 'prin_a', now(), now() + interval '1 hour')" },
        { "an expiry before issue", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{Id('c')}', 'recovery', {Hash}, now(), now())" },
        { "consumed and superseded", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at, consumed_at, superseded_at) VALUES ('{Id('c')}', 'recovery', {Hash}, now(), now() + interval '1 hour', now(), now())" },
        { "a second active bootstrap token", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{Id('c')}', 'bootstrap', {Hash}, now(), now() + interval '1 hour')" },
        { "a second active enrollment code", $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, principal_id, issued_at, expires_at) VALUES ('{Id('c')}', 'enrollment', {Hash}, 'prin_a', now(), now() + interval '1 hour')" },
    };

    [Theory]
    [MemberData(nameof(Violating))]
    public async Task EachConstraintAndPartialIndexRejectsAViolatingRow(string reason, string insert)
    {
        Assert.NotEmpty(reason);
        await using var database = await MigratedWithRowsAsync();

        await Assert.ThrowsAsync<PostgresException>(async () => await fixture.ExecuteAsync(database.Name, insert));
    }

    [Fact]
    public async Task InactiveTokensDoNotCountAgainstThePartialIndexes()
    {
        await using var database = await MigratedWithRowsAsync();

        await fixture.ExecuteAsync(database.Name, "UPDATE isk_identity.tokens SET superseded_at = now() WHERE purpose = 'bootstrap'");
        await fixture.ExecuteAsync(database.Name, $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{Id('c')}', 'bootstrap', {Hash}, now(), now() + interval '1 hour')");
        await fixture.ExecuteAsync(database.Name, "UPDATE isk_identity.tokens SET consumed_at = now() WHERE purpose = 'enrollment'");
        await fixture.ExecuteAsync(database.Name, $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, principal_id, issued_at, expires_at) VALUES ('{Id('d')}', 'enrollment', {Hash}, 'prin_a', now(), now() + interval '1 hour')");
        await fixture.ExecuteAsync(database.Name, $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{Id('e')}', 'recovery', {Hash}, now(), now() + interval '1 hour')");

        Assert.Equal(5L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_identity.tokens"));
    }

    [Fact]
    public async Task TheRuntimeLoginCanInsertAndReadTheInstallationButNeverUpdateDeleteOrTruncateIt()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(Set).ApplyAsync(new MigrationApplyRequest(), Token);

        await using var connection = new NpgsqlConnection(database.RuntimeConnectionString());
        await connection.OpenAsync(Token);
        await using (var insert = new NpgsqlCommand("INSERT INTO isk_identity.installation (state, initialized_at, initialized_by) VALUES ('initialized', now(), 'prin_a')", connection))
        {
            Assert.Equal(1, await insert.ExecuteNonQueryAsync(Token));
        }

        foreach (var statement in new[]
        {
            "UPDATE isk_identity.installation SET initialized_by = 'prin_b'",
            "DELETE FROM isk_identity.installation",
            "TRUNCATE isk_identity.installation",
        })
        {
            await using var command = new NpgsqlCommand(statement, connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Token));
            Assert.True(failure.SqlState is PostgresErrorCodes.InsufficientPrivilege, $"{statement}: {failure.SqlState}");
        }

        await using (var update = new NpgsqlCommand($"INSERT INTO isk_identity.principals VALUES ('prin_a', 'operator', now(), 'bootstrap'); UPDATE isk_identity.principals SET created_at = now()", connection))
        {
            Assert.Equal(2, await update.ExecuteNonQueryAsync(Token));
        }

        Assert.Equal("prin_a", await fixture.ScalarAsync<string>(database.Name, "SELECT initialized_by FROM isk_identity.installation"));
    }

    // A migrated database with an installation row, principal prin_a and its grant, an active bootstrap token, and prin_a's enrollment code.
    private async Task<ScratchDatabase> MigratedWithRowsAsync()
    {
        fixture.RequirePostgreSql();
        var database = await ScratchDatabase.CreateAsync(fixture);
        await using (var store = TestStore.For(database))
        {
            await store.Runner(Set).ApplyAsync(new MigrationApplyRequest(), Token);
        }

        await fixture.ExecuteAsync(database.Name,
            "INSERT INTO isk_identity.installation (state, initialized_at, initialized_by) VALUES ('initialized', now(), 'prin_a'); " +
            "INSERT INTO isk_identity.principals VALUES ('prin_a', 'operator', now(), 'bootstrap'); " +
            "INSERT INTO isk_identity.operator_grants VALUES ('prin_a', 'system_operator', now()); " +
            $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{Id('a')}', 'bootstrap', {Hash}, now(), now() + interval '1 hour'); " +
            $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, principal_id, issued_at, expires_at) VALUES ('{Id('b')}', 'enrollment', {Hash}, 'prin_a', now(), now() + interval '1 hour')");
        return database;
    }
}
