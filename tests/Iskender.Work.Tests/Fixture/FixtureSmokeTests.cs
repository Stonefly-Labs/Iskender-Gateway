using Iskender.Infrastructure.PostgreSql;

namespace Iskender.Work.Tests.Fixture;

/// <summary>The work fixture's database has its four modules applied, and opens a unit in each of its four scopes (ISK-0009 T011).</summary>
public class FixtureSmokeTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDirectoryCoordinationWorkAndAppliesModulesAreApplied()
    {
        work.RequirePostgreSql();

        Assert.Equal("applies,coordination,directory,work", await work.ScalarAsync<string>("SELECT string_agg(module, ',' ORDER BY module) FROM isk_migrations.history"));
    }

    [Fact]
    public async Task AUnitOpensInEachScopeAndSeesOnlyItsOwnAppliesRows()
    {
        work.RequirePostgreSql();

        foreach (var scope in WorkFixture.Scopes)
        {
            await using var unit = await work.OpenAsync(scope);
            var adapter = PostgreSqlUnitOfWork.From(unit);
            Assert.Equal(1, await adapter.Command(AppliesModule.Applies, "INSERT INTO isk_applies.applies (scope_key, item_id, attempt) VALUES ($1, gen_random_uuid(), 1)").ExecuteAsync(Token));
            Assert.Equal(1L, await adapter.Command(AppliesModule.Applies, "SELECT count(*) FROM isk_applies.applies WHERE {scope}").ScalarAsync<long>(Token));
            await unit.CommitAsync(Token);
        }

        Assert.Equal(4L, await work.ScalarAsync<long>("SELECT count(DISTINCT scope_key) FROM isk_applies.applies"));
    }
}
