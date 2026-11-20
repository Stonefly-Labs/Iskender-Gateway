using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Directory;

/// <summary>
/// Register and settle (ISK-0009 T012, research R3, persistence contract Isolation rule 10, FR-006, FR-007). Each test
/// uses its own module name, so the tests of the class share one database without sharing rows.
/// </summary>
public class ScopeDirectoryTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static readonly DateTime Early = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Late = new(2030, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegisterInsertsTheUnitsOwnRowWithItsDueTime()
    {
        work.RequirePostgreSql();

        await RegisterAsync(WorkFixture.OrganizationA, "t_insert", Late);

        Assert.Equal($"org:{Owners.OrgA}|{Late:O}", await RowAsync("t_insert"));
    }

    [Fact]
    public async Task RegisterLowersALaterDueTimeAndAnEmptyOne()
    {
        work.RequirePostgreSql();
        await RegisterAsync(WorkFixture.OrganizationA, "t_lower", Late);

        await RegisterAsync(WorkFixture.OrganizationA, "t_lower", Early);
        Assert.Equal($"org:{Owners.OrgA}|{Early:O}", await RowAsync("t_lower"));

        await work.ExecuteAsync("UPDATE isk_directory.scopes SET due_at = NULL WHERE module = 't_lower'");
        await RegisterAsync(WorkFixture.OrganizationA, "t_lower", Late);
        Assert.Equal($"org:{Owners.OrgA}|{Late:O}", await RowAsync("t_lower"));
    }

    [Fact]
    public async Task RegisterDoesNotRaiseAnEarlierDueTime()
    {
        work.RequirePostgreSql();
        await RegisterAsync(WorkFixture.OrganizationA, "t_raise", Early);

        await RegisterAsync(WorkFixture.OrganizationA, "t_raise", Late);

        Assert.Equal($"org:{Owners.OrgA}|{Early:O}", await RowAsync("t_raise"));
    }

    [Fact]
    public async Task SettleDeletesTheRowWhenNothingRemains()
    {
        work.RequirePostgreSql();
        await RegisterAsync(WorkFixture.ProjectA1, "t_delete", Early);

        Assert.Equal(DirectorySettlement.Removed, await SettleAsync(WorkFixture.ProjectA1, "t_delete", DirectoryRemaining.None));

        Assert.Equal(0L, await work.ScalarAsync<long>("SELECT count(*) FROM isk_directory.scopes WHERE module = 't_delete'"));
    }

    [Fact]
    public async Task SettleKeepsTheRowWithTheEarliestDueTimeOrNone()
    {
        work.RequirePostgreSql();
        await RegisterAsync(WorkFixture.Operator, "t_keep", Early);

        Assert.Equal(DirectorySettlement.Kept, await SettleAsync(WorkFixture.Operator, "t_keep", DirectoryRemaining.Due(Late)));
        Assert.Equal($"operator|{Late:O}", await RowAsync("t_keep"));

        Assert.Equal(DirectorySettlement.Kept, await SettleAsync(WorkFixture.Operator, "t_keep", DirectoryRemaining.Due(null)));
        Assert.Equal("operator|", await RowAsync("t_keep"));
    }

    [Fact]
    public async Task SettleOfAMissingRowRunsNoNextQuery()
    {
        work.RequirePostgreSql();
        var asked = false;

        await using var unit = await work.OpenAsync(WorkFixture.OrganizationB);
        var settlement = await ScopeDirectory.SettleAsync(unit, "t_absent", _ =>
        {
            asked = true;
            return ValueTask.FromResult(DirectoryRemaining.None);
        }, Token);

        Assert.Equal(DirectorySettlement.Absent, settlement);
        Assert.False(asked);
    }

    [Fact]
    public async Task SettleOnlyTouchesTheUnitsOwnScope()
    {
        work.RequirePostgreSql();
        await RegisterAsync(WorkFixture.OrganizationA, "t_own", Early);
        await RegisterAsync(WorkFixture.OrganizationB, "t_own", Early);

        Assert.Equal(DirectorySettlement.Removed, await SettleAsync(WorkFixture.OrganizationB, "t_own", DirectoryRemaining.None));

        Assert.Equal($"org:{Owners.OrgA}|{Early:O}", await RowAsync("t_own"));
    }

    [Theory]
    [InlineData(UnitOfWorkIsolation.RepeatableRead)]
    [InlineData(UnitOfWorkIsolation.Serializable)]
    public async Task SettleInAUnitThatIsNotReadCommittedFailsBeforeAnyStatement(UnitOfWorkIsolation isolation)
    {
        work.RequirePostgreSql();
        await RegisterAsync(WorkFixture.OrganizationA, "t_isolation", Early);

        await using var unit = await work.OpenAsync(WorkFixture.OrganizationA, new UnitOfWorkOptions("applies", "test.settle") { Isolation = isolation });
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await ScopeDirectory.SettleAsync(unit, "t_isolation", _ => ValueTask.FromResult(DirectoryRemaining.None), Token));

        Assert.Same(ErrorCodes.Internal, failure.Code);
        Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        Assert.Equal(0, PostgreSqlUnitOfWork.From(unit).StatementsSent);
    }

    [Theory]
    [InlineData(UnitOfWorkIsolation.RepeatableRead)]
    [InlineData(UnitOfWorkIsolation.Serializable)]
    public async Task RegisterSucceedsAtStricterIsolationWithoutARace(UnitOfWorkIsolation isolation)
    {
        work.RequirePostgreSql();
        var module = isolation == UnitOfWorkIsolation.Serializable ? "t_serializable" : "t_repeatable";

        await RegisterAsync(WorkFixture.ProjectA1, module, Late, isolation);
        await RegisterAsync(WorkFixture.ProjectA1, module, Early, isolation);

        Assert.Equal($"project:{Owners.OrgA}/{Owners.ProjectA1}|{Early:O}", await RowAsync(module));
    }

    private async Task RegisterAsync(UnitOfWorkScope scope, string module, DateTime due, UnitOfWorkIsolation isolation = UnitOfWorkIsolation.ReadCommitted)
    {
        await using var unit = await work.OpenAsync(scope, new UnitOfWorkOptions("applies", "test.register") { Isolation = isolation });
        await ScopeDirectory.RegisterAsync(unit, module, due, Token);
        await unit.CommitAsync(Token);
    }

    private async Task<DirectorySettlement> SettleAsync(UnitOfWorkScope scope, string module, DirectoryRemaining remaining)
    {
        await using var unit = await work.OpenAsync(scope);
        var settlement = await ScopeDirectory.SettleAsync(unit, module, _ => ValueTask.FromResult(remaining), Token);
        await unit.CommitAsync(Token);
        return settlement;
    }

    // The module's rows as the superuser sees them, ordered by scope: "<listed_scope>|<due_at in ISO 8601 UTC>".
    private Task<string> RowAsync(string module) => work.ScalarAsync<string>(
        $"SELECT string_agg(listed_scope || '|' || coalesce(to_char(due_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"0Z\"'), ''), ',' ORDER BY listed_scope) FROM isk_directory.scopes WHERE module = '{module}'");
}
