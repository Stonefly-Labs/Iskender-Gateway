using System.Collections.Concurrent;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;
using ConformanceDiagnostics = Iskender.Coordination.Conformance.CapturingDiagnostics;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// The PostgreSQL coordinator under the conformance suites, as a class fixture: one database with the coordination
/// module applied, and each client index on a data source of its own, standing in for a replica. Tests in a class share
/// the database and keep apart by module. Events go to the conformance suite's capturing logger. With
/// <see cref="OwnsFixture"/>, it supports faults, stopping and pausing that fixture's container, never the shared one.
/// </summary>
public sealed class PostgreSqlCoordinatorUnderTest(PostgreSqlFixture fixture) : ICoordinatorUnderTest, IAsyncLifetime
{
    /// <summary>Short enough bounds that expiry tests take well under a second.</summary>
    public const string Section =
        "\"leases\":{\"coordinator\":\"postgresql\",\"minimumDuration\":\"300ms\",\"maximumDuration\":\"1m\",\"safetyMargin\":\"100ms\",\"maximumWait\":\"5s\"}";

    private readonly ConcurrentDictionary<int, Client> _clients = new();
    private ScratchDatabase? _database;
    private bool _paused;

    /// <summary>Whether the fixture is this coordinator's own, so it may be stopped and paused.</summary>
    public bool OwnsFixture { get; init; }

    public PostgreSqlFixture Fixture => fixture;

    public ClassDatabase Database => _database ?? throw new InvalidOperationException("The database was not created.");

    public CoordinationSettings CoordinationSettings { get; } = ReadSettings(Section);

    public ConformanceDiagnostics.Logger Logger { get; } = new();

    public CoordinatorKind Kind => CoordinatorKind.PostgreSql;

    public CoordinatorCapabilities Capabilities => CoordinatorCapabilities.PostgreSql;

    public bool IsDistributed => true;

    public ConformanceSettings Settings => new(
        CoordinationSettings.MinimumLeaseDuration,
        CoordinationSettings.MaximumLeaseDuration,
        CoordinationSettings.SafetyMargin,
        CoordinationSettings.MaximumWait,
        CoordinationSettings.MaximumExpiry,
        CoordinationSettings.MaximumValueBytes,
        CoordinationSettings.OperationTimeout);

    public ConformanceScopes Scopes { get; } = new();

    public async ValueTask InitializeAsync()
    {
        if (fixture.UnavailableReason is not null)
        {
            return;
        }

        _database = await ScratchDatabase.CreateAsync(fixture);
        await ApplyAsync(_database);
    }

    public void Require() => fixture.RequirePostgreSql();

    public CoordinatorClient CreateClient(int index) => new(ClientAt(index).Atomic, Leases(index), ClientAt(index).Cache);

    /// <summary>Client <paramref name="index"/>'s lease coordinator, on its own data source.</summary>
    public PostgreSqlLeaseCoordinator Leases(int index) => ClientAt(index).Leases;

    /// <summary>Client <paramref name="index"/>'s versioned cache, with an in-memory store of its own.</summary>
    public PostgreSqlVersionedCache Cache(int index) => ClientAt(index).Cache;

    /// <summary>Client <paramref name="index"/>'s unit-of-work factory.</summary>
    public PostgreSqlUnitOfWorkFactory Units(int index) => ClientAt(index).Store.Factory;

    /// <summary>The fencing guard, writing to the conformance suite's capturing logger.</summary>
    public PostgreSqlFencingGuard Guard => new(Logger);

    public async ValueTask PassTimeAsync(TimeSpan duration) => await Task.Delay(duration, CancellationToken.None);

    public bool SupportsFaults => OwnsFixture;

    public async ValueTask StopAsync()
    {
        RequireFaults();
        await fixture.StopAsync();
    }

    public async ValueTask PauseAsync()
    {
        RequireFaults();
        await fixture.PauseAsync();
        _paused = true;
    }

    /// <summary>Unpauses a paused container, or starts a stopped one again; its port may change, so clients are not reused.</summary>
    public async ValueTask ResumeAsync()
    {
        RequireFaults();
        if (_paused)
        {
            await fixture.UnpauseAsync();
            _paused = false;
        }
        else
        {
            await fixture.RestartAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            await client.Store.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    /// <summary>
    /// Applies the coordination module to <paramref name="database"/> as the migration login, with the scoped fixture
    /// module, whose <c>slots</c> table holds the rows a fence protects.
    /// </summary>
    public static async Task ApplyAsync(ClassDatabase database)
    {
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module, FixtureModules.Scoped])).ApplyAsync(new MigrationApplyRequest(), CancellationToken.None);
    }

    /// <summary>Settings from a validated <c>coordination</c> section with <paramref name="fields"/>.</summary>
    public static CoordinationSettings ReadSettings(string fields)
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("coordination.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"coordination\":{" + fields + "}}}"),
            environmentName: "development"));
        Assert.True(report.IsValid, report.ToString());
        return CoordinationSettings.From(report.Snapshot!);
    }

    private void RequireFaults()
    {
        if (!OwnsFixture)
        {
            throw new NotSupportedException("Only a coordinator under test that owns its fixture supports faults.");
        }
    }

    private Client ClientAt(int index) => _clients.GetOrAdd(index, _ => new Client(Database, CoordinationSettings, Logger));

    private sealed class Client
    {
        // Scenarios run up to 20 clients, each with a pool of its own, while other classes run in parallel against the same
        // server, so each pool is capped well below the default to stay inside the server's connection limit.
        private static readonly Dictionary<string, object> Pool = new(StringComparer.Ordinal) { ["poolMaximum"] = 2L };

        public Client(ClassDatabase database, CoordinationSettings settings, ConformanceDiagnostics.Logger logger)
        {
            Store = TestStore.For(database, Pool);
            Leases = new PostgreSqlLeaseCoordinator(Store.Factory, settings, logger);
            Atomic = new PostgreSqlAtomicOperations(Store.Factory, settings);

            // Each client has an in-memory store of its own, as each replica does.
            Cache = new PostgreSqlVersionedCache(Store.Factory, settings, logger);
        }

        public TestStore Store { get; }

        public PostgreSqlLeaseCoordinator Leases { get; }

        public PostgreSqlAtomicOperations Atomic { get; }

        public PostgreSqlVersionedCache Cache { get; }
    }
}
