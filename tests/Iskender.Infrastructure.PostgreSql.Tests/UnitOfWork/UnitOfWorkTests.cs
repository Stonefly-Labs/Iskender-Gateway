using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.UnitOfWork;

public class UnitOfWorkTests(EmptyDatabase database) : IClassFixture<EmptyDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> ScopeKinds() => ["organization", "project", "operator"];

    [Theory]
    [MemberData(nameof(ScopeKinds))]
    public async Task OpeningSetsTheThreeTransactionLocalScopeSettings(string kind)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var (scope, organization, project) = kind switch
        {
            "organization" => (Owners.Organization(Owners.OrgA), Owners.OrgA.Value, ""),
            "project" => (Owners.Project(Owners.OrgA, Owners.ProjectA1), Owners.OrgA.Value, Owners.ProjectA1.Value),
            _ => (UnitOfWorkScope.Operator, "", ""),
        };

        await using var unit = await store.OpenAsync(scope);
        var settings = await PostgreSqlUnitOfWork.From(unit)
            .FrameworkCommand("SELECT current_setting('iskender.scope_kind'), current_setting('iskender.organization_id'), current_setting('iskender.project_id')")
            .QueryAsync(row => (row.GetString(0), row.GetString(1), row.GetString(2)), Token);

        Assert.Equal((kind, organization, project), Assert.Single(settings));
    }

    [Theory]
    [InlineData(UnitOfWorkIsolation.ReadCommitted, "read committed")]
    [InlineData(UnitOfWorkIsolation.RepeatableRead, "repeatable read")]
    [InlineData(UnitOfWorkIsolation.Serializable, "serializable")]
    public async Task EachIsolationLevelIsTheTransactions(UnitOfWorkIsolation isolation, string expected)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);

        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA), TestStore.Options(isolation: isolation));

        Assert.Equal(isolation, unit.Isolation);
        Assert.Equal(expected, await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("SELECT current_setting('transaction_isolation')").ScalarAsync<string>(Token));
    }

    [Fact]
    public async Task ACommittedChangeIsVisibleToAnotherUnit()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var table = await ProbeTableAsync();

        await using (var writer = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            await PostgreSqlUnitOfWork.From(writer).FrameworkCommand($"INSERT INTO {table} VALUES ($1)").With("committed").ExecuteAsync(Token);
            await writer.CommitAsync(Token);
            Assert.Equal(UnitOfWorkState.Committed, writer.State);
        }

        Assert.Equal(1, await CountAsync(store, table));
    }

    [Fact]
    public async Task AUnitDisposedWithoutCommitLeavesNothing()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var table = await ProbeTableAsync();

        var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        await PostgreSqlUnitOfWork.From(unit).FrameworkCommand($"INSERT INTO {table} VALUES ($1)").With("dropped").ExecuteAsync(Token);
        await unit.DisposeAsync();

        Assert.Equal(UnitOfWorkState.Closed, unit.State);
        Assert.Equal(0, await CountAsync(store, table));
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4002);
    }

    [Fact]
    public async Task ACommittedUnitAcceptsNoFurtherCallsAndClosesOnDispose()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        await unit.CommitAsync(Token);

        Assert.Same(ErrorCodes.Internal, (await Assert.ThrowsAsync<GatewayException>(async () => await unit.CommitAsync(Token))).Code);
        Assert.Same(ErrorCodes.Internal, (await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("SELECT 1").ExecuteAsync(Token))).Code);
        Assert.Equal(UnitOfWorkState.Committed, unit.State);
        Assert.Null(unit.Failure);

        await unit.DisposeAsync();
        await unit.DisposeAsync();
        Assert.Equal(UnitOfWorkState.Closed, unit.State);
    }

    [Fact]
    public async Task AFailedStatementFaultsTheUnitAndRecordsTheFailureFirst()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("SELECT 1 / 0").ExecuteAsync(Token));

        Assert.Same(ErrorCodes.Internal, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        Assert.Same(failure.Code, unit.Failure!.Code);
        Assert.Equal(PersistenceFailureClass.Unexpected, unit.Failure.FailureClass);
        Assert.Same(ErrorCodes.Internal, (await Assert.ThrowsAsync<GatewayException>(async () => await unit.CommitAsync(Token))).Code);
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4003);
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4014 && logged.Fields.Any(field => field.Key == "SqlStateClass" && (string?)field.Value == "22"));
    }

    [Fact]
    public async Task ASecondConcurrentCallFailsAndFaultsTheUnit()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var adapter = PostgreSqlUnitOfWork.From(unit);

        var first = adapter.FrameworkCommand("SELECT pg_sleep(0.5)").ExecuteAsync(Token);
        var second = await Assert.ThrowsAsync<GatewayException>(async () => await adapter.FrameworkCommand("SELECT 1").ExecuteAsync(Token));
        await first;

        Assert.Same(ErrorCodes.Internal, second.Code);
        Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        Assert.Equal(PersistenceFailureClass.Unexpected, unit.Failure!.FailureClass);
    }

    [Fact]
    public async Task NoScopeFailsWithScopeRequiredBeforeAnyConnection()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.Factory.OpenAsync(null!, TestStore.Options(), Token));

        Assert.Same(ErrorCodes.PersistenceScopeRequired, failure.Code);
        Assert.Empty(store.Credentials.Requests);
    }

    [Fact]
    public async Task AnUnresolvedRequestContextFailsBeforeAnyConnection()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var context = new Core.Contracts.Context.GatewayExecutionContext(
            new Core.Contracts.Context.CorrelationContext(Core.Contracts.Context.RequestCorrelationId.New(), System.Diagnostics.ActivityTraceId.CreateRandom(), System.Diagnostics.ActivitySpanId.CreateRandom()),
            DateTimeOffset.UtcNow.AddMinutes(1),
            TimeProvider.System,
            Token);

        var failure = Assert.Throws<GatewayException>(() => UnitOfWorkScope.ForRequest(context));

        Assert.Same(ErrorCodes.PersistenceScopeRequired, failure.Code);
        Assert.Empty(store.Credentials.Requests);
    }

    [Fact]
    public async Task AUnitKeepsTheScopeItWasOpenedWith()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var scope = Owners.Project(Owners.OrgA, Owners.ProjectA2);

        await using var unit = await store.OpenAsync(scope);

        Assert.Same(scope, unit.Scope);
        Assert.IsType<ProjectScope>(unit.Scope.Owner);
        Assert.Equal(UnitOfWorkState.Open, unit.State);
    }

    private async Task<string> ProbeTableAsync()
    {
        var table = $"public.probe_{Guid.NewGuid():N}";
        await database.Fixture.ExecuteAsync(database.Name, $"CREATE TABLE {table} (id text primary key); GRANT SELECT, INSERT ON {table} TO {PostgreSqlFixture.RuntimeGroup}");
        return table;
    }

    private static async Task<long> CountAsync(TestStore store, string table)
    {
        await using var reader = await store.OpenAsync(UnitOfWorkScope.Operator);
        return await PostgreSqlUnitOfWork.From(reader).FrameworkCommand($"SELECT count(*) FROM {table}").ScalarAsync<long>(Token);
    }
}
