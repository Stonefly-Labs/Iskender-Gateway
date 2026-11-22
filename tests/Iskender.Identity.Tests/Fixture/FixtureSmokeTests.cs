using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Identity.Tests.Fixture;

/// <summary>The operator fixture's database has the five modules and their schemas, and its unreachable factory fails to open (ISK-0010 T014).</summary>
public class FixtureSmokeTests(OperatorFixture operators) : IClassFixture<OperatorFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFiveModulesAndTheirSchemasAreApplied()
    {
        operators.RequirePostgreSql();

        Assert.Equal("audit,coordination,directory,identity,work", await operators.ScalarAsync<string>("SELECT string_agg(module, ',' ORDER BY module) FROM isk_migrations.history"));
        Assert.Equal(
            "isk_audit,isk_coordination,isk_directory,isk_identity,isk_migrations,isk_work",
            await operators.ScalarAsync<string>("SELECT string_agg(nspname, ',' ORDER BY nspname) FROM pg_namespace WHERE nspname LIKE 'isk\\_%'"));
        Assert.Equal("[]", await operators.RowsAsync("isk_identity.installation"));
    }

    [Fact]
    public async Task AnOperatorUnitOpensAndTheUnreachableFactoryFails()
    {
        operators.RequirePostgreSql();

        await using (var unit = await operators.OpenAsync())
        {
            await unit.CommitAsync(Token);
        }

        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await operators.UnreachableFactory.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions("identity", "test.op"), Token));
        Assert.Same(ErrorCodes.PersistenceUnavailable, failure.Code);
    }

    [Fact]
    public void TheConfigurationReadsTheOperatorSection()
    {
        var configuration = OperatorFixture.Configuration("\"bootstrap\":{\"tokenLifetime\":\"10m\"}");

        Assert.Equal(TimeSpan.FromMinutes(10), configuration.Settings.TokenLifetime);
        Assert.Equal(TimeSpan.FromMinutes(10), configuration.Lifetimes.TokenLifetime);
        Assert.Equal(TimeSpan.FromHours(24), configuration.Lifetimes.EnrollmentCodeLifetime);
    }
}
