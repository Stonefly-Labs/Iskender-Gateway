using System.Collections.Concurrent;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using ConformanceDiagnostics = Iskender.Coordination.Conformance.CapturingDiagnostics;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// The PostgreSQL coordinator under the conformance suites, as a class fixture: one database with the coordination
/// module applied, and each client index on a data source of its own, standing in for a replica. Tests in a class share
/// the database and keep apart by module. Events go to the conformance suite's capturing logger.
/// </summary>
public sealed class PostgreSqlCoordinatorUnderTest(PostgreSqlFixture fixture) : ICoordinatorUnderTest, IAsyncLifetime
{
    /// <summary>Short enough bounds that expiry tests take well under a second.</summary>
    public const string Section =
        "\"leases\":{\"coordinator\":\"postgresql\",\"minimumDuration\":\"300ms\",\"maximumDuration\":\"1m\",\"safetyMargin\":\"100ms\",\"maximumWait\":\"5s\"}";

    private readonly ConcurrentDictionary<int, Client> _clients = new();
    private ScratchDatabase? _database;

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

    public CoordinatorClient CreateClient(int index) => new(null!, Leases(index), null!);

    /// <summary>Client <paramref name="index"/>'s lease coordinator, on its own data source.</summary>
    public PostgreSqlLeaseCoordinator Leases(int index) => _clients.GetOrAdd(index, _ => new Client(Database, CoordinationSettings, Logger)).Leases;

    public async ValueTask PassTimeAsync(TimeSpan duration) => await Task.Delay(duration, CancellationToken.None);

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

    /// <summary>Applies the coordination module to <paramref name="database"/> as the migration login.</summary>
    public static async Task ApplyAsync(ClassDatabase database)
    {
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), CancellationToken.None);
    }

    /// <summary>Settings from a validated <c>coordination</c> section with <paramref name="fields"/>.</summary>
    public static CoordinationSettings ReadSettings(string fields)
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("coordination.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"coordination\":{" + fields + "}}}"),
            environmentName: "development"));
        Assert.True(report.IsValid, report.ToString());
        return CoordinationSettings.From(report.Snapshot!.GetSection(CoordinationSectionSchema.Schema));
    }

    private sealed class Client
    {
        public Client(ClassDatabase database, CoordinationSettings settings, ConformanceDiagnostics.Logger logger)
        {
            Store = TestStore.For(database);
            Leases = new PostgreSqlLeaseCoordinator(Store.Factory, settings, logger);
        }

        public TestStore Store { get; }

        public PostgreSqlLeaseCoordinator Leases { get; }
    }
}
