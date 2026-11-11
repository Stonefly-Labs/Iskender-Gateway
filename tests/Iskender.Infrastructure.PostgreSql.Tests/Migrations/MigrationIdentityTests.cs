using System.Text.RegularExpressions;
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

    [Fact]
    public void MigrationsRunOnlyWhenTheRunnerIsCalled()
    {
        // FR-021: nothing in the adapter calls the runner, and only the runner's run and status methods open the
        // migration data source. No constructor or static initializer does.
        var sources = AdapterSources();
        Assert.NotEmpty(sources);

        Assert.All(sources, source => Assert.DoesNotMatch(@"\.(ApplyAsync|ReverseAsync)\s*\(", source.Text));

        var opens = sources.SelectMany(source => Regex.Matches(source.Text, @"\b_?[sS]ources\.Migration\b").Select(match => (source.Path, Member: EnclosingMember(source.Text, match.Index)))).ToList();
        Assert.Equal(
            [("Migrations/PostgreSqlMigrationRunner.cs", "GetStatusAsync"), ("Migrations/PostgreSqlMigrationRunner.cs", "RunAsync")],
            opens.Order());

        var checkedTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, text) in sources)
        {
            foreach (Match constructor in Regex.Matches(text, @"^\s+(?:public|internal|private|protected|static)[^\n=;(]*?\b(?<type>[A-Z]\w*)\((?=[^\n]*\)\s*(?::|$|=>))", RegexOptions.Multiline))
            {
                if (!Regex.IsMatch(text, $@"\b(class|record|struct)\s+{constructor.Groups["type"].Value}\b"))
                {
                    continue;
                }

                checkedTypes.Add(constructor.Groups["type"].Value);
                var body = Body(text, constructor.Index);
                Assert.DoesNotContain("OpenConnection", body, StringComparison.Ordinal);
                Assert.DoesNotMatch(@"\.Migration\b(?!Username|Password|LockWait)", body.Replace("DatabaseCredentialPurpose.Migration", "", StringComparison.Ordinal));
            }

            Assert.DoesNotMatch(@"static[^\n;]*=[^\n;]*(OpenConnection|[sS]ources\.Migration)", text);
        }

        Assert.Superset(new HashSet<string>(["PostgreSqlDataSources", "PostgreSqlMigrationRunner", "PostgreSqlUnitOfWorkFactory", "MigrationModule", "MigrationSet", "MigrationLock"]), checkedTypes);
    }

    private static List<(string Path, string Text)> AdapterSources()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Iskender.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The repository root was not found.");
        }

        var adapter = Path.Combine(root, "src", "Iskender.Infrastructure.PostgreSql");
        return [.. Directory.EnumerateFiles(adapter, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetRelativePath(adapter, path).Replace('\\', '/'), File.ReadAllText(path)))];
    }

    // The name of the last member declared at class-member indentation before the index.
    private static string EnclosingMember(string text, int index) =>
        Regex.Matches(text[..index], @"^    (?:public|private|internal|protected)[^\n(=;]*?\b(?<name>\w+)(?:<[^>\n]*>)?\(", RegexOptions.Multiline)[^1].Groups["name"].Value;

    // From the declaration to its matching closing brace, or to the end of an expression body.
    private static string Body(string text, int start)
    {
        var open = text.IndexOf('{', start);
        var arrow = text.IndexOf("=>", start, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
        {
            return text[start..text.IndexOf(';', arrow)];
        }

        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return text[start..(i + 1)];
            }
        }

        return text[start..];
    }
}
