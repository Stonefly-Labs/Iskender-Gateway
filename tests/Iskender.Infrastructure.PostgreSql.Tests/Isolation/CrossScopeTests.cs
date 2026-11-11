using System.Reflection;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Beta;

namespace Iskender.Infrastructure.PostgreSql.Tests.Isolation;

/// <summary>
/// User Story 3 scenarios 1 to 4 and SC-004. Every cross-scope read, update, delete, and reference by ID reads as not
/// found or changes nothing, once with the repositories' and <c>VersionedRows</c>' owner predicate and once with
/// row-level security alone.
/// </summary>
public class CrossScopeTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<bool> OwnerPredicate() => [true, false];

    [Theory]
    [MemberData(nameof(OwnerPredicate))]
    public async Task AnOrganizationReachesNoOtherOrganizationsRowsAndNoProjectRows(bool ownerPredicate)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var seed = await SeedAsync(store);
        var before = await StateAsync(seed);
        var widgets = new WidgetRepository(ownerPredicate);
        var counters = new CounterRepository(ownerPredicate);
        var notes = new WidgetNoteRepository(ownerPredicate);

        foreach (var (org, foreignWidget, foreignCounter, ownWidget) in new[]
        {
            (Owners.OrgA, seed.WidgetB, seed.CounterB, seed.WidgetA),
            (Owners.OrgB, seed.WidgetA, seed.CounterA, seed.WidgetB),
        })
        {
            await using var unit = await store.OpenAsync(Owners.Organization(org));
            PostgreSqlUnitOfWork.From(unit).OwnerPredicate = ownerPredicate;

            await AssertNotFoundLikeMissingAsync(() => widgets.GetAsync(unit, foreignWidget, Token), () => widgets.GetAsync(unit, seed.Missing, Token));
            await AssertVersionedNotFoundAsync(unit, foreignWidget);
            await AssertNotFoundLikeMissingAsync(() => counters.GetAsync(unit, foreignCounter, Token), () => counters.GetAsync(unit, seed.Missing, Token));
            Assert.Equal(0, await widgets.RenameAsync(unit, foreignWidget, "stolen", Token));
            Assert.Equal(0, await widgets.DeleteAsync(unit, foreignWidget, Token));
            Assert.Equal(0, await counters.IncrementAsync(unit, foreignCounter, Token));
            Assert.Equal(0, await counters.DeleteAsync(unit, foreignCounter, Token));
            Assert.All(await widgets.ListAsync(unit, Token), widget => Assert.Equal(org.Value, widget.OwnerOrganizationId));
            Assert.Contains(await widgets.ListAsync(unit, Token), widget => widget.Id == ownWidget);

            // An organization scope sees no project-owned row, even its own projects' (FR-006).
            Assert.Empty(await notes.ListAsync(unit, Token));
            await AssertNotFoundLikeMissingAsync(() => notes.GetAsync(unit, seed.NoteA1, Token), () => notes.GetAsync(unit, seed.Missing, Token));
            Assert.Equal(0, await notes.UpdateAsync(unit, seed.NoteA1, "stolen", Token));
            Assert.Equal(0, await notes.DeleteAsync(unit, seed.NoteA1, Token));
            Assert.Equal(UnitOfWorkState.Open, unit.State);
            await unit.CommitAsync(Token);
        }

        Assert.Equal(before, await StateAsync(seed));
    }

    [Theory]
    [MemberData(nameof(OwnerPredicate))]
    public async Task AProjectReachesItsOrganizationsRowsButNoOtherProjectsOrOrganizations(bool ownerPredicate)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var seed = await SeedAsync(store);
        var before = await StateAsync(seed);
        var widgets = new WidgetRepository(ownerPredicate);
        var notes = new WidgetNoteRepository(ownerPredicate);

        foreach (var (project, own, foreign) in new[]
        {
            (Owners.ProjectA1, seed.NoteA1, seed.NoteA2),
            (Owners.ProjectA2, seed.NoteA2, seed.NoteA1),
        })
        {
            await using var unit = await store.OpenAsync(Owners.Project(Owners.OrgA, project));
            PostgreSqlUnitOfWork.From(unit).OwnerPredicate = ownerPredicate;

            await AssertNotFoundLikeMissingAsync(() => notes.GetAsync(unit, foreign, Token), () => notes.GetAsync(unit, seed.Missing, Token));
            await AssertVersionedNotFoundAsync(unit, seed.WidgetB);
            Assert.Equal(0, await notes.UpdateAsync(unit, foreign, "stolen", Token));
            Assert.Equal(0, await notes.DeleteAsync(unit, foreign, Token));
            Assert.Equal([own], (await notes.ListAsync(unit, Token)).Select(note => note.Id).Where(id => id.StartsWith(seed.Prefix, StringComparison.Ordinal)));

            // Its own organization's organization-owned rows are visible; another organization's are not (FR-006).
            Assert.Equal(seed.WidgetA, (await widgets.GetAsync(unit, seed.WidgetA, Token)).Id);
            await AssertNotFoundLikeMissingAsync(() => widgets.GetAsync(unit, seed.WidgetB, Token), () => widgets.GetAsync(unit, seed.Missing, Token));
            Assert.Equal(0, await widgets.RenameAsync(unit, seed.WidgetB, "stolen", Token));

            // A reference by ID to another organization's widget reads as not found, though the foreign key alone would accept it.
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(() => notes.AddAsync(unit, seed.Prefix + "n-ref", seed.WidgetB, "x", Token))).Code);
            await unit.CommitAsync(Token);
        }

        Assert.Equal(before, await StateAsync(seed));
    }

    [Fact]
    public async Task TheOperatorScopeIsRefusedATenantTableAndRowSecurityAloneShowsItNothing()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var seed = await SeedAsync(store);
        var before = await StateAsync(seed);

        await using (var unit = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            var failure = await Assert.ThrowsAsync<GatewayException>(() => new WidgetRepository().ListAsync(unit, Token));
            Assert.Same(ErrorCodes.PersistenceScopeNotPermitted, failure.Code);
            Assert.Equal(0, PostgreSqlUnitOfWork.From(unit).StatementsSent);
        }

        await using (var unit = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            PostgreSqlUnitOfWork.From(unit).ScopePreCheck = false;
            var widgets = new WidgetRepository(ownerPredicate: false);
            var counters = new CounterRepository(ownerPredicate: false);
            var notes = new WidgetNoteRepository(ownerPredicate: false);

            Assert.Empty(await widgets.ListAsync(unit, Token));
            Assert.Empty(await notes.ListAsync(unit, Token));
            Assert.Equal(0, await widgets.RenameAsync(unit, seed.WidgetA, "stolen", Token));
            Assert.Equal(0, await widgets.DeleteAsync(unit, seed.WidgetB, Token));
            Assert.Equal(0, await counters.IncrementAsync(unit, seed.CounterA, Token));
            Assert.Equal(0, await notes.DeleteAsync(unit, seed.NoteA1, Token));
            await unit.CommitAsync(Token);
        }

        Assert.Equal(before, await StateAsync(seed));
    }

    [Fact]
    public async Task AUnitCannotBeOpenedWithoutAScopeOrFromAnUnresolvedContext()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var unresolved = new GatewayExecutionContext(
            new CorrelationContext(RequestCorrelationId.New(), System.Diagnostics.ActivityTraceId.CreateRandom(), System.Diagnostics.ActivitySpanId.CreateRandom()),
            DateTimeOffset.UtcNow.AddMinutes(1),
            TimeProvider.System,
            Token);

        Assert.Same(ErrorCodes.PersistenceScopeRequired, (await Assert.ThrowsAsync<GatewayException>(async () => await store.Factory.OpenAsync(null!, TestStore.Options(), Token))).Code);
        Assert.Same(ErrorCodes.PersistenceScopeRequired, Assert.Throws<GatewayException>(() => UnitOfWorkScope.ForRequest(unresolved)).Code);
        Assert.Empty(store.Credentials.Requests);
    }

    [Fact]
    public void NoApiBuildsAScopeFromACallerSuppliedOwner()
    {
        Type[] callerSupplied = [typeof(OwnerScope), typeof(OrganizationScope), typeof(ProjectScope), typeof(ResourceId), typeof(string)];

        Assert.All(typeof(IUnitOfWorkFactory).GetMethods(), method =>
            Assert.DoesNotContain(method.GetParameters(), parameter => callerSupplied.Any(type => type.IsAssignableFrom(parameter.ParameterType))));
        Assert.Empty(typeof(UnitOfWorkScope).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(typeof(UnitOfWorkScope).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => !method.IsSpecialName), method =>
            Assert.DoesNotContain(method.GetParameters(), parameter => callerSupplied.Any(type => type.IsAssignableFrom(parameter.ParameterType))));
        Assert.Equal(
            ["ForRequest", "ForResource"],
            typeof(UnitOfWorkScope).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => !method.IsSpecialName && method.DeclaringType == typeof(UnitOfWorkScope)).Select(method => method.Name).Order());
        Assert.All(typeof(PostgreSqlUnitOfWorkFactory).GetMethods(BindingFlags.Public | BindingFlags.Instance), method =>
            Assert.DoesNotContain(method.GetParameters(), parameter => typeof(OwnerScope).IsAssignableFrom(parameter.ParameterType)));
    }

    private static async Task AssertNotFoundLikeMissingAsync<T>(Func<Task<T>> foreign, Func<Task<T>> missing)
    {
        var foreignFailure = await Assert.ThrowsAsync<GatewayException>(foreign);
        var missingFailure = await Assert.ThrowsAsync<GatewayException>(missing);
        Assert.Same(ErrorCodes.ResourceNotFound, foreignFailure.Code);
        Assert.Same(missingFailure.Code, foreignFailure.Code);
        Assert.Equal(missingFailure.Message, foreignFailure.Message);
    }

    // A versioned update and delete of another organization's row, with its current token, read as not found exactly
    // like a missing row, and leave the unit open (FR-010, FR-016). The caller's StateAsync check shows 0 rows changed.
    private static async Task AssertVersionedNotFoundAsync(IUnitOfWork unit, string foreignWidget)
    {
        foreach (var id in new[] { foreignWidget, foreignWidget + "-missing" })
        {
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(async () => await WidgetRepository.RenameVersionedAsync(unit, id, VersionToken.Initial, "stolen", Token))).Code);
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(async () => await WidgetRepository.DeleteVersionedAsync(unit, id, VersionToken.Initial, Token))).Code);
        }

        Assert.Equal(UnitOfWorkState.Open, unit.State);
    }

    // Rows for organizations A and B and projects A/P1 and A/P2, written through units in each owner's own scope.
    private static async Task<Seed> SeedAsync(TestStore store)
    {
        var seed = new Seed($"x{Guid.NewGuid():N}"[..13] + "-");
        var notes = new WidgetNoteRepository();
        foreach (var (org, widget, counter) in new[] { (Owners.OrgA, seed.WidgetA, seed.CounterA), (Owners.OrgB, seed.WidgetB, seed.CounterB) })
        {
            await using var unit = await store.OpenAsync(Owners.Organization(org));
            await WidgetRepository.InsertAsync(unit, widget, "original", Token);
            await CounterRepository.CreateAsync(unit, counter, Token);
            await unit.CommitAsync(Token);
        }

        foreach (var (project, note) in new[] { (Owners.ProjectA1, seed.NoteA1), (Owners.ProjectA2, seed.NoteA2) })
        {
            await using var unit = await store.OpenAsync(Owners.Project(Owners.OrgA, project));
            await notes.AddAsync(unit, note, seed.WidgetA, "original", Token);
            await unit.CommitAsync(Token);
        }

        return seed;
    }

    // Every seeded row, read by the superuser, so a test can show nothing changed.
    private async Task<string> StateAsync(Seed seed) =>
        await database.Fixture.ScalarAsync<string>(
            database.Name,
            $"SELECT concat_ws(' | ', " +
            $"(SELECT string_agg(id || ':' || owner_organization_id || ':' || name || ':' || row_version, ',' ORDER BY id) FROM isk_alpha.widgets WHERE id LIKE '{seed.Prefix}%'), " +
            $"(SELECT string_agg(id || ':' || owner_project_id || ':' || body, ',' ORDER BY id) FROM isk_alpha.widget_notes WHERE id LIKE '{seed.Prefix}%'), " +
            $"(SELECT string_agg(id || ':' || value, ',' ORDER BY id) FROM isk_beta.counters WHERE id LIKE '{seed.Prefix}%'))");

    private sealed record Seed(string Prefix)
    {
        public string WidgetA => Prefix + "w-a";

        public string WidgetB => Prefix + "w-b";

        public string CounterA => Prefix + "c-a";

        public string CounterB => Prefix + "c-b";

        public string NoteA1 => Prefix + "n-a1";

        public string NoteA2 => Prefix + "n-a2";

        public string Missing => Prefix + "missing";
    }
}
