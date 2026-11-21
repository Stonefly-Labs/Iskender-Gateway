using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Work.Tests.Fixture;

/// <summary>
/// The work fixture (work contract, Test fixture rule 1; ISK-0009 research R18): one database per test class, built on
/// ISK-0004's PostgreSQL fixture, with the <c>directory</c>, <c>coordination</c>, and <c>work</c> modules and the
/// fixture-only <c>applies</c> module applied, and units for the operator, two organizations, and a project. A test class
/// takes it as its class fixture.
/// </summary>
public sealed class WorkFixture(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private ScratchDatabase? _database;

    public PostgreSqlFixture Postgres { get; } = fixture;

    /// <summary>The class database. Valid only after <see cref="RequirePostgreSql"/>.</summary>
    public ClassDatabase Database => _database ?? throw new InvalidOperationException("The work fixture has no database.");

    public string DatabaseName => Database.Name;

    internal PostgreSqlDataSources Sources { get; private set; } = null!;

    internal PostgreSqlUnitOfWorkFactory Factory { get; private set; } = null!;

    internal CapturingDiagnostics.Logger Logger { get; } = new();

    /// <summary>The release under test, plus the fixture's <c>applies</c> module.</summary>
    internal static MigrationSet Migrations { get; } = new([DirectoryMigrations.Module, CoordinationMigrations.Module, WorkMigrations.Module, AppliesModule.Module]);

    public static UnitOfWorkScope Operator => UnitOfWorkScope.Operator;

    public static UnitOfWorkScope OrganizationA { get; } = Owners.Organization(Owners.OrgA);

    public static UnitOfWorkScope OrganizationB { get; } = Owners.Organization(Owners.OrgB);

    public static UnitOfWorkScope ProjectA1 { get; } = Owners.Project(Owners.OrgA, Owners.ProjectA1);

    /// <summary>The four scopes the fixture opens units in.</summary>
    public static IReadOnlyList<UnitOfWorkScope> Scopes { get; } = [Operator, OrganizationA, OrganizationB, ProjectA1];

    public async ValueTask InitializeAsync()
    {
        if (Postgres.UnavailableReason is not null)
        {
            return;
        }

        _database = await ScratchDatabase.CreateAsync(Postgres);
        Sources = new PostgreSqlDataSources(TestSettings.For(_database), TestCredentialProvider.For(Postgres));
        Factory = new PostgreSqlUnitOfWorkFactory(Sources, TimeProvider.System, Logger);
        await new PostgreSqlMigrationRunner(Sources, Migrations, TimeProvider.System, Logger).ApplyAsync(new MigrationApplyRequest(), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await Sources.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    /// <summary>Skips or fails the calling test unless the container is running.</summary>
    public void RequirePostgreSql() => Postgres.RequirePostgreSql();

    /// <summary>Opens a unit in <paramref name="scope"/>, for module <c>applies</c> unless <paramref name="options"/> says otherwise.</summary>
    public ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions? options = null) =>
        Factory.OpenAsync(scope, options ?? new UnitOfWorkOptions("applies", "test.op"), TestContext.Current.CancellationToken);

    /// <summary>Runs <paramref name="sql"/> as the superuser, outside every scope's row-level security, for setup and checks.</summary>
    public Task<T> ScalarAsync<T>(string sql) => Postgres.ScalarAsync<T>(DatabaseName, sql);

    /// <summary>Runs <paramref name="sql"/> as the superuser.</summary>
    public Task ExecuteAsync(string sql) => Postgres.ExecuteAsync(DatabaseName, sql);

    /// <summary>Enqueues one item of <paramref name="kind"/> in <paramref name="scope"/> and commits, returning its ID.</summary>
    internal async Task<Guid> EnqueueAsync(UnitOfWorkScope scope, WorkKind kind, string key)
    {
        var outbox = new PostgreSqlWorkOutbox([new WorkKindDeclaration(kind, 1)], logger: null);
        await using var unit = await OpenAsync(scope);
        var item = (await outbox.EnqueueAsync(unit, new WorkItemRequest(scope.Owner, kind, 1, new byte[] { 1 }, key), TestContext.Current.CancellationToken)).ItemId;
        await unit.CommitAsync(TestContext.Current.CancellationToken);
        return item;
    }

    /// <summary>A new organization's scope, whose shard leases no earlier test of the class holds.</summary>
    internal static UnitOfWorkScope FreshOrganization() => Owners.Organization(Iskender.Core.Contracts.Identifiers.ResourceId.New(Iskender.Core.Contracts.Identifiers.ResourceTypes.Organization));

    /// <summary>
    /// Makes pending item <paramref name="itemId"/> due now, and its scope's <c>work</c> directory row with it, since a
    /// settle has set that row to the item's later due time.
    /// </summary>
    internal Task MakeDueAsync(Guid itemId) => ExecuteAsync(
        $"UPDATE isk_work.items SET due_at = clock_timestamp() WHERE item_id = '{itemId}' AND state = 'pending'; "
        + $"UPDATE isk_directory.scopes SET due_at = clock_timestamp() WHERE module = 'work' AND listed_scope = (SELECT scope_key FROM isk_work.items WHERE item_id = '{itemId}')");

    /// <summary>The number of <c>applies</c> rows of <paramref name="itemId"/>.</summary>
    internal Task<long> AppliesAsync(Guid itemId) => ScalarAsync<long>($"SELECT count(*) FROM isk_applies.applies WHERE item_id = '{itemId}'");

    /// <summary>Item <paramref name="itemId"/>'s state and attempt count, as <c>state|count</c>.</summary>
    internal Task<string> ItemAsync(Guid itemId) => ScalarAsync<string>($"SELECT coalesce((SELECT state || '|' || attempt_count FROM isk_work.items WHERE item_id = '{itemId}'), 'absent')");
}
