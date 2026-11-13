using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Outage;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>The outage suite against the PostgreSQL coordinator, on a container of its own, so stopping or pausing it leaves every other class's database alone.</summary>
public sealed class PostgreSqlOutageConformanceTests : OutageConformanceTests, IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSql = new();
    private PostgreSqlCoordinatorUnderTest? _coordinator;

    protected override ICoordinatorUnderTest Coordinator => _coordinator ?? throw new InvalidOperationException("The coordinator was not created.");

    public async ValueTask InitializeAsync()
    {
        await _postgreSql.InitializeAsync();
        _coordinator = new PostgreSqlCoordinatorUnderTest(_postgreSql) { OwnsFixture = true };
        await _coordinator.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_coordinator is not null)
        {
            await _coordinator.DisposeAsync();
        }

        await _postgreSql.DisposeAsync();
    }
}
