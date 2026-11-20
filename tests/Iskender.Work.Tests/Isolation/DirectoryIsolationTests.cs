using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Isolation;

/// <summary>
/// Scope directory isolation (ISK-0009 T037, US4.3, US4.4, FR-006, FR-008, research R2): each unit writes and locks only
/// its own scope's row, a tenant or project unit reads only its own, an operator unit reads every row, and a malformed
/// row is skipped with event 8009 and no unit. Each assertion runs with the <c>{listed}</c> predicate present and
/// removed, so row-level security alone holds.
/// </summary>
public class DirectoryIsolationTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private const string Module = "probe";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<bool> Predicate() => [true, false];

    [Theory]
    [MemberData(nameof(Predicate))]
    public async Task ATenantUnitReadsOnlyItsOwnRowAndChangesNoOtherScopesRow(bool predicate)
    {
        work.RequirePostgreSql();
        var (a, b) = (WorkFixture.FreshOrganization(), WorkFixture.FreshOrganization());
        var listedA = await RegisterAsync(a);
        var listedB = await RegisterAsync(b);
        var before = await RowsAsync();

        await using (var unit = await work.OpenAsync(a, new UnitOfWorkOptions("work", "test.directory")))
        {
            Assert.Equal([listedA], await ReadAsync(unit, predicate));
            Assert.Equal(0, await ChangeOthersAsync(unit, listedB, predicate));
            await unit.CommitAsync(Token);
        }

        Assert.False(await InsertOtherAsync(a, listedB));

        Assert.Equal(before, await RowsAsync());
    }

    [Theory]
    [MemberData(nameof(Predicate))]
    public async Task AProjectUnitSeesNoneOfItsOrganizationsRows(bool predicate)
    {
        work.RequirePostgreSql();
        var organization = Iskender.Core.Contracts.Identifiers.ResourceId.New(Iskender.Core.Contracts.Identifiers.ResourceTypes.Organization);
        var project = Owners.Project(organization, Iskender.Core.Contracts.Identifiers.ResourceId.New(Iskender.Core.Contracts.Identifiers.ResourceTypes.Project));
        var listedOrganization = await RegisterAsync(Owners.Organization(organization));
        var listedProject = await RegisterAsync(project);

        await using var unit = await work.OpenAsync(project, new UnitOfWorkOptions("work", "test.directory"));
        var visible = await ReadAsync(unit, predicate);

        Assert.Equal([listedProject], visible);
        Assert.DoesNotContain(listedOrganization, visible);
    }

    [Theory]
    [MemberData(nameof(Predicate))]
    public async Task AnOperatorUnitReadsEveryRowAndChangesOnlyTheOperatorRow(bool predicate)
    {
        work.RequirePostgreSql();
        await work.ExecuteAsync($"DELETE FROM isk_directory.scopes WHERE module = '{Module}' AND listed_scope = 'operator'");
        var listedTenant = await RegisterAsync(WorkFixture.FreshOrganization());
        var listedOperator = await RegisterAsync(WorkFixture.Operator);
        var before = await RowsAsync();

        await using (var unit = await work.OpenAsync(WorkFixture.Operator, new UnitOfWorkOptions("work", "test.directory")))
        {
            var every = await PostgreSqlUnitOfWork.From(unit).Command(DirectoryTables.Scopes, "SELECT listed_scope FROM isk_directory.scopes WHERE {any_listed} AND module = $1")
                .With(Module).QueryAsync(reader => reader.GetString(0), Token);
            Assert.Contains(listedTenant, every);
            Assert.Contains(listedOperator, every);
            Assert.Equal(0, await ChangeOthersAsync(unit, listedTenant, predicate));
            Assert.Equal(1, await PostgreSqlUnitOfWork.From(unit).Command(DirectoryTables.Scopes, "UPDATE isk_directory.scopes SET due_at = NULL WHERE module = $2 AND " + Listed(predicate))
                .With(Module).ExecuteAsync(Token));
            await unit.CommitAsync(Token);
        }

        Assert.False(await InsertOtherAsync(WorkFixture.Operator, listedTenant));
        Assert.Equal(before.Replace(listedOperator + "|set", listedOperator + "|null", StringComparison.Ordinal), await RowsAsync());
    }

    [Fact]
    public async Task AMalformedRowIsSkippedWith8009NamingOnlyTheModuleAndNoUnit()
    {
        work.RequirePostgreSql();
        await work.ExecuteAsync("UPDATE isk_directory.scopes SET due_at = NULL WHERE module = 'work'");
        await work.ExecuteAsync("INSERT INTO isk_directory.scopes (listed_scope, module, due_at) VALUES ('org:not-an-id', 'work', clock_timestamp())");
        var before = work.Logger.Events.Count;

        await using (var dispatcher = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast))
        {
            await dispatcher.PassAsync();
            Assert.Equal(["work.directory_page"], dispatcher.Units.Opened.Select(opened => opened.Operation));
            Assert.All(dispatcher.Units.Opened, opened => Assert.Same(UnitOfWorkScope.Operator, opened.Scope));
        }

        var skipped = Assert.Single(work.Logger.Events.Skip(before), logged => logged.Id.Id == 8009);
        Assert.Equal([new("Module", "work")], skipped.Fields.Where(field => field.Key != "{OriginalFormat}").Select(field => new KeyValuePair<string, string?>(field.Key, field.Value?.ToString())));
        await work.ExecuteAsync("DELETE FROM isk_directory.scopes WHERE listed_scope = 'org:not-an-id'");
    }

    private static string Listed(bool predicate) => predicate ? "{listed}" : "$1::text IS NOT NULL";

    // Tries to update, lock, and delete another scope's row, returning the rows changed or locked.
    private static async Task<int> ChangeOthersAsync(IUnitOfWork unit, string other, bool predicate)
    {
        var adapter = PostgreSqlUnitOfWork.From(unit);
        var changed = 0;
        changed += await adapter.Command(DirectoryTables.Scopes, "UPDATE isk_directory.scopes SET due_at = NULL WHERE listed_scope = $2 AND " + Listed(predicate)).With(other).ExecuteAsync(Token);
        changed += (await adapter.Command(DirectoryTables.Scopes, "SELECT 1 FROM isk_directory.scopes WHERE listed_scope = $2 AND " + Listed(predicate) + " FOR UPDATE").With(other).QueryAsync(_ => 1, Token)).Count;
        changed += await adapter.Command(DirectoryTables.Scopes, "DELETE FROM isk_directory.scopes WHERE listed_scope = $2 AND " + Listed(predicate)).With(other).ExecuteAsync(Token);
        return changed;
    }

    // Tries to insert a row for another scope in a unit of scope; the policy's check refuses it, which faults the unit, so
    // it has a unit of its own. Returns whether any such row exists afterwards.
    private async Task<bool> InsertOtherAsync(UnitOfWorkScope scope, string other)
    {
        await using (var unit = await work.OpenAsync(scope, new UnitOfWorkOptions("work", "test.insert")))
        {
            try
            {
                await PostgreSqlUnitOfWork.From(unit).Command(DirectoryTables.Scopes, "INSERT INTO isk_directory.scopes (listed_scope, module, due_at) SELECT $2, 'probe_insert', clock_timestamp() WHERE $1::text IS NOT NULL")
                    .With(other).ExecuteAsync(Token);
                await unit.CommitAsync(Token);
            }
            catch (Iskender.Core.Contracts.Errors.GatewayException)
            {
                // Refused, as expected; the check below is what the test asserts.
            }
        }

        return await work.ScalarAsync<long>($"SELECT count(*) FROM isk_directory.scopes WHERE module = 'probe_insert' AND listed_scope = '{other}'") > 0;
    }

    // Registers scope's probe row and returns its listed_scope: the one row the registration added.
    private async Task<string> RegisterAsync(UnitOfWorkScope scope)
    {
        var before = (await RowsAsync()).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(row => row.Split('|')[0]).ToHashSet(StringComparer.Ordinal);
        await using (var unit = await work.OpenAsync(scope, new UnitOfWorkOptions("work", "test.register")))
        {
            await ScopeDirectory.RegisterAsync(unit, Module, DateTime.UtcNow.AddMinutes(5), Token);
            await unit.CommitAsync(Token);
        }

        return Assert.Single((await RowsAsync()).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(row => row.Split('|')[0]), listed => !before.Contains(listed));
    }

    private static async Task<List<string>> ReadAsync(IUnitOfWork unit, bool predicate) =>
        [.. await PostgreSqlUnitOfWork.From(unit).Command(DirectoryTables.Scopes, "SELECT listed_scope FROM isk_directory.scopes WHERE module = $2 AND " + Listed(predicate))
            .With(Module).QueryAsync(reader => reader.GetString(0), Token)];

    private Task<string> RowsAsync() =>
        work.ScalarAsync<string>($"SELECT coalesce(string_agg(listed_scope || '|' || CASE WHEN due_at IS NULL THEN 'null' ELSE 'set' END, ',' ORDER BY listed_scope), '') FROM isk_directory.scopes WHERE module = '{Module}'");
}
