using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Isolation;

/// <summary>Research R6: the scope pre-check refuses a table its scope may not use before the command exists, sending nothing.</summary>
public class ScopePreCheckTests(EmptyDatabase database) : IClassFixture<EmptyDatabase>
{
    private static readonly TenantTable Widgets = new("alpha", "widgets");
    private static readonly OperatorTable Settings = new("alpha", "settings");

    public static TheoryData<string, bool> Refused() => new()
    {
        { "operator", true },
        { "organization", false },
        { "project", false },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task ATableTheScopeMayNotUseIsRefusedBeforeAnyStatement(string kind, bool tenantTable)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        await using var unit = await store.OpenAsync(Scope(kind));
        var adapter = PostgreSqlUnitOfWork.From(unit);

        var failure = Assert.Throws<GatewayException>(() => adapter.Command(tenantTable ? Widgets : Settings, "SELECT 1"));

        Assert.Same(ErrorCodes.PersistenceScopeNotPermitted, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.Equal(0, adapter.StatementsSent);
        Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        Assert.Equal(PersistenceFailureClass.ScopeViolation, unit.Failure!.FailureClass);
        Assert.Same(ErrorCodes.PersistenceScopeNotPermitted, unit.Failure.Code);
    }

    [Theory]
    [InlineData("operator", false)]
    [InlineData("organization", true)]
    [InlineData("project", true)]
    public async Task ATableTheScopeMayUseIsAllowed(string kind, bool tenantTable)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        await using var unit = await store.OpenAsync(Scope(kind));
        var adapter = PostgreSqlUnitOfWork.From(unit);

        Assert.NotNull(adapter.Command(tenantTable ? Widgets : Settings, "SELECT 1"));
        Assert.Equal(UnitOfWorkState.Open, unit.State);
        Assert.Equal(0, adapter.StatementsSent);

        await adapter.Command(tenantTable ? Widgets : Settings, "SELECT 1").ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, adapter.StatementsSent);
    }

    [Fact]
    public async Task TheTestSeamTurnsThePreCheckOffForOneUnitOnly()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        await using var bypassed = await store.OpenAsync(UnitOfWorkScope.Operator);
        await using var checkedUnit = await store.OpenAsync(UnitOfWorkScope.Operator);

        PostgreSqlUnitOfWork.From(bypassed).ScopePreCheck = false;

        Assert.NotNull(PostgreSqlUnitOfWork.From(bypassed).Command(Widgets, "SELECT 1"));
        Assert.Throws<GatewayException>(() => PostgreSqlUnitOfWork.From(checkedUnit).Command(Widgets, "SELECT 1"));
    }

    [Fact]
    public void ADescriptorNamesItsModulesSchemaAndTable()
    {
        Assert.Equal("isk_alpha.widgets", Widgets.QualifiedName);
        Assert.Equal(("isk_alpha", "settings"), (Settings.Schema, Settings.Table));
    }

    [Theory]
    [InlineData("Alpha", "widgets")]
    [InlineData("alpha", "Widgets")]
    [InlineData("alpha", "widgets; DROP TABLE x")]
    [InlineData("alpha", "")]
    [InlineData("", "widgets")]
    [InlineData("alpha", "isk_beta.widgets")]
    public void AnInvalidDescriptorIsRefused(string module, string table)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new TenantTable(module, table)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new OperatorTable(module, table)).Code);
    }

    private static UnitOfWorkScope Scope(string kind) => kind switch
    {
        "organization" => Owners.Organization(Owners.OrgA),
        "project" => Owners.Project(Owners.OrgA, Owners.ProjectA1),
        _ => UnitOfWorkScope.Operator,
    };
}
