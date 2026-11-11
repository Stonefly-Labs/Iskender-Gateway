using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>Contract Failures rule 2 for the PostgreSQL coordinator's units of work (ISK-0005 T013).</summary>
public class CoordinationUnitsTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, PersistenceFailureClass, string, CoordinationFailureClass> Mapped() => new()
    {
        { "persistence.unavailable", PersistenceFailureClass.Unavailable, "coordination.unavailable", CoordinationFailureClass.Unavailable },
        { "persistence.unavailable", PersistenceFailureClass.AuthenticationFailed, "coordination.unavailable", CoordinationFailureClass.AuthenticationFailed },
        { "persistence.timeout", PersistenceFailureClass.Timeout, "coordination.timeout", CoordinationFailureClass.Timeout },
        { "persistence.cancelled", PersistenceFailureClass.Cancelled, "coordination.cancelled", CoordinationFailureClass.Cancelled },
        { "persistence.concurrency_conflict", PersistenceFailureClass.ConcurrencyConflict, "core.internal", CoordinationFailureClass.Unexpected },
        { "persistence.unique_conflict", PersistenceFailureClass.UniqueConflict, "core.internal", CoordinationFailureClass.Unexpected },
        { "persistence.scope_not_permitted", PersistenceFailureClass.ScopeViolation, "core.internal", CoordinationFailureClass.ScopeViolation },
        { "core.internal", PersistenceFailureClass.Unexpected, "core.internal", CoordinationFailureClass.Unexpected },
    };

    [Theory]
    [MemberData(nameof(Mapped))]
    public void AUnitFailureMapsToItsCoordinationCodeKeepingItsClass(string code, PersistenceFailureClass failureClass, string expectedCode, CoordinationFailureClass expectedClass)
    {
        var failure = CoordinationUnits.Map(new PersistenceFailure(ErrorCodes.All.Single(candidate => candidate.Id == code), failureClass));

        Assert.Equal(expectedCode, failure.Code.Id);
        Assert.Equal(expectedClass, CoordinationFailures.ClassOf(failure));
        Assert.False(CoordinationFailures.IsOutcomeUnknown(failure));
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public void AnUnknownCommitOutcomeIsMarked()
    {
        var failure = CoordinationUnits.Map(new PersistenceFailure(ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.Unavailable, commitOutcomeUnknown: true));

        Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
        Assert.True(CoordinationFailures.IsOutcomeUnknown(failure));
    }

    [Theory]
    [InlineData("persistence.unavailable", "coordination.unavailable", CoordinationFailureClass.Unavailable)]
    [InlineData("persistence.timeout", "coordination.timeout", CoordinationFailureClass.Timeout)]
    [InlineData("persistence.cancelled", "coordination.cancelled", CoordinationFailureClass.Cancelled)]
    [InlineData("persistence.scope_required", "core.internal", CoordinationFailureClass.Unexpected)]
    public async Task AFailureToOpenTheUnitMapsByItsCode(string code, string expectedCode, CoordinationFailureClass expectedClass)
    {
        var factory = new FailingFactory(ErrorCodes.All.Single(candidate => candidate.Id == code));

        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await CoordinationUnits.RunAsync(factory, CoordinationScope.Operator, "atomic.add", TimeSpan.FromSeconds(5), (_, _) => ValueTask.FromResult(1), Token));

        Assert.Equal(expectedCode, failure.Code.Id);
        Assert.Equal(expectedClass, CoordinationFailures.ClassOf(failure));
    }

    [Fact]
    public void TheOptionsAreTheCoordinationModuleReadCommittedAndTheOperationTimeout()
    {
        var options = CoordinationUnits.Options(CoordinationScope.Operator, "leases.acquire", TimeSpan.FromMilliseconds(750));

        Assert.Equal("coordination", options.Module);
        Assert.Equal("leases.acquire", options.Operation);
        Assert.Equal(UnitOfWorkIsolation.ReadCommitted, options.Isolation);
        Assert.Equal(TimeSpan.FromMilliseconds(750), options.Timeout);
        Assert.Null(options.Deadline);
        Assert.False(options.SafeToRerun);
    }

    [Fact]
    public async Task AWorkResultIsCommittedInTheScopesOwnUnit()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);
        var scope = CoordinationScope.From(Owners.Organization(Owners.OrgA));

        var inserted = await CoordinationUnits.RunAsync(store.Factory, scope, "atomic.add", TimeSpan.FromSeconds(5), async (unit, token) =>
        {
            Assert.Equal("coordination", PostgreSqlUnitOfWork.From(unit).Module);
            return await PostgreSqlUnitOfWork.From(unit).Command(CoordinationTables.Values, "INSERT INTO isk_coordination.coordination_values (scope_key, key_hash, value, version, expires_at) VALUES ($1, 'h', 7, 1, now() + interval '1 minute')").ExecuteAsync(token);
        }, Token);

        Assert.Equal(1, inserted);
        Assert.Equal($"org:{Owners.OrgA.Value}", await fixture.ScalarAsync<string>(database.Name, "SELECT scope_key FROM isk_coordination.coordination_values WHERE value = 7"));
    }

    [Fact]
    public async Task ACoordinationFailureFromTheWorkPassesThroughAndNothingCommits()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await CoordinationUnits.RunAsync<int>(store.Factory, CoordinationScope.Operator, "fencing.accept", TimeSpan.FromSeconds(5), async (unit, token) =>
            {
                await PostgreSqlUnitOfWork.From(unit).Command(CoordinationTables.Fences, "INSERT INTO isk_coordination.fences (scope_key, resource_hash, lease_hash, highest_token) VALUES ($1, 'r', 'l', 1)").ExecuteAsync(token);
                throw CoordinationFailures.Raise(ErrorCodes.CoordinationFencingRejected, CoordinationFailureClass.FencingRejected);
            }, Token));

        Assert.Same(ErrorCodes.CoordinationFencingRejected, failure.Code);
        Assert.Equal(CoordinationFailureClass.FencingRejected, CoordinationFailures.ClassOf(failure));
        Assert.Equal(0L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_coordination.fences"));
    }

    [Fact]
    public async Task AStatementFailureInTheUnitBecomesAContractFailure()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await CoordinationUnits.RunAsync(store.Factory, CoordinationScope.Operator, "atomic.add", TimeSpan.FromSeconds(5), async (unit, token) =>
                await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("SELECT 1 / 0").ScalarAsync<int>(token), Token));

        Assert.Same(ErrorCodes.Internal, failure.Code);
        Assert.Equal(CoordinationFailureClass.Unexpected, CoordinationFailures.ClassOf(failure));
    }

    private sealed class FailingFactory(ErrorCode code) : IUnitOfWorkFactory
    {
        public ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken) =>
            throw new GatewayException(code);
    }
}
