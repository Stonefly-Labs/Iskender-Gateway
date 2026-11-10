using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Beta;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules;

/// <summary>The fixture repositories work within their own scope. Cross-scope behavior is in the Isolation tests.</summary>
public class RepositoryTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WidgetsCanBeInsertedReadListedRenamedAndDeletedInTheirOrganization()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var widgets = new WidgetRepository();
        var org = Owners.Organization(Owners.OrgA);

        await using (var unit = await store.OpenAsync(org))
        {
            await WidgetRepository.InsertAsync(unit, "w-repo-1", "one", Token);
            await WidgetRepository.InsertAsync(unit, "w-repo-2", "two", Token);
            await unit.CommitAsync(Token);
        }

        await using (var unit = await store.OpenAsync(org))
        {
            Assert.Equal(new Widget("w-repo-1", Owners.OrgA.Value, "one", 1), await widgets.GetAsync(unit, "w-repo-1", Token));
            Assert.Equal(["w-repo-1", "w-repo-2"], (await widgets.ListAsync(unit, Token)).Select(widget => widget.Id).Where(id => id.StartsWith("w-repo-", StringComparison.Ordinal)));
            Assert.Equal(1, await widgets.RenameAsync(unit, "w-repo-1", "uno", Token));
            Assert.Equal(1, await widgets.DeleteAsync(unit, "w-repo-2", Token));
            Assert.Equal(0, await widgets.DeleteAsync(unit, "w-repo-missing", Token));
            Assert.Equal(new Widget("w-repo-1", Owners.OrgA.Value, "uno", 2), await widgets.GetAsync(unit, "w-repo-1", Token));
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(() => widgets.GetAsync(unit, "w-repo-2", Token))).Code);
        }
    }

    [Fact]
    public async Task ANoteRefersToAWidgetItsScopeCanSee()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var notes = new WidgetNoteRepository();
        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            await WidgetRepository.InsertAsync(unit, "w-note-1", "noted", Token);
            await unit.CommitAsync(Token);
        }

        await using var project = await store.OpenAsync(Owners.Project(Owners.OrgA, Owners.ProjectA1));
        await notes.AddAsync(project, "n-1", "w-note-1", "hello", Token);

        Assert.Equal(new WidgetNote("n-1", Owners.OrgA.Value, Owners.ProjectA1.Value, "w-note-1", "hello"), await notes.GetAsync(project, "n-1", Token));
        Assert.Single(await notes.ListAsync(project, Token));
        Assert.Equal(1, await notes.UpdateAsync(project, "n-1", "bye", Token));
        Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(() => notes.AddAsync(project, "n-2", "w-missing", "x", Token))).Code);
        Assert.Equal(UnitOfWorkState.Open, project.State);
        Assert.Equal(1, await notes.DeleteAsync(project, "n-1", Token));
    }

    [Fact]
    public async Task CountersSettingsAndTheLockLogWork()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var counters = new CounterRepository();

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgB)))
        {
            await CounterRepository.CreateAsync(unit, "c-repo", Token);
            Assert.Equal(1, await counters.IncrementAsync(unit, "c-repo", Token));
            Assert.Equal(1, await counters.GetAsync(unit, "c-repo", Token));
            await LockLogRepository.AppendAsync(unit, "first", Token);
            await LockLogRepository.AppendAsync(unit, "second", Token);
            Assert.Equal(["first", "second"], await LockLogRepository.ReadAsync(unit, Token));
            Assert.Equal(1, await counters.DeleteAsync(unit, "c-repo", Token));
        }

        await using var operatorUnit = await store.OpenAsync(UnitOfWorkScope.Operator);
        await SettingRepository.PutAsync(operatorUnit, "mode", "a", Token);
        await SettingRepository.PutAsync(operatorUnit, "mode", "b", Token);
        Assert.Equal("b", await SettingRepository.GetAsync(operatorUnit, "mode", Token));
    }
}
