using Iskender.Domain.Ports;
using Iskender.Infrastructure.Composition;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Health;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>Adds the PostgreSQL adapter's persistence and coordinator to a host's service collection (hosting contract, Registration rule 6; research R4). Owned by ISK-0006.</summary>
public static class PostgreSqlServiceCollectionExtensions
{
    /// <summary>The module <see cref="AddIskenderPostgreSqlPersistence"/> composes.</summary>
    public const string PersistenceModule = "persistence-postgresql";

    /// <summary>The module <see cref="AddIskenderPostgreSqlCoordination"/> composes.</summary>
    public const string CoordinationModule = "coordination-postgresql";

    /// <summary>
    /// Composes module <c>persistence-postgresql</c>, which requires <c>hosting</c>: the <c>persistence</c> section's
    /// settings, the data sources, the unit-of-work factory, the migration runner over every registered
    /// <see cref="MigrationModule"/>, and the health probe, each a container singleton. The data sources resolve an
    /// <see cref="IDatabaseCredentialProvider"/> if the host registered one. Opens no connection and reads no credential. A
    /// second call fails with <c>hosting.registration_duplicate</c>.
    /// </summary>
    public static IServiceCollection AddIskenderPostgreSqlPersistence(this IServiceCollection services)
    {
        CompositionRegistration.Add(services, new CompositionModule(PersistenceModule, [PostgreSqlSectionSchema.Schema], ["hosting"]));

        services.AddSingleton(provider => PostgreSqlSettings.From(provider.GetRequiredService<IValidatedConfiguration>().Snapshot.GetSection(PostgreSqlSectionSchema.Schema)));
        services.AddSingleton(provider => new PostgreSqlDataSources(provider.GetRequiredService<PostgreSqlSettings>(), provider.GetService<IDatabaseCredentialProvider>()));
        services.AddSingleton(provider => new PostgreSqlUnitOfWorkFactory(provider.GetRequiredService<PostgreSqlDataSources>(), provider.GetService<TimeProvider>(), Logger(provider, PostgreSqlAdapter.DiagnosticsName)));
        services.AddSingleton<IUnitOfWorkFactory>(provider => provider.GetRequiredService<PostgreSqlUnitOfWorkFactory>());
        services.AddSingleton(provider => new MigrationSet(provider.GetServices<MigrationModule>(), Logger(provider, PostgreSqlAdapter.DiagnosticsName)));
        services.AddSingleton<IMigrationRunner>(provider => new PostgreSqlMigrationRunner(
            provider.GetRequiredService<PostgreSqlDataSources>(),
            provider.GetRequiredService<MigrationSet>(),
            provider.GetService<TimeProvider>(),
            Logger(provider, PostgreSqlAdapter.DiagnosticsName)));
        services.AddSingleton<IPersistenceHealthProbe>(provider => new PostgreSqlHealthProbe(
            provider.GetRequiredService<PostgreSqlDataSources>(),
            provider.GetRequiredService<MigrationSet>(),
            provider.GetService<TimeProvider>(),
            Logger(provider, PostgreSqlAdapter.DiagnosticsName)));

        return services;
    }

    /// <summary>
    /// Composes module <c>coordination-postgresql</c>, which requires <c>coordination</c> and
    /// <c>persistence-postgresql</c>: a <see cref="CoordinatorRegistration"/> of kind <c>postgresql</c> whose atomic
    /// operations, leases, fencing guard, cache, and health probe share the persistence data sources, and the
    /// <c>coordination</c> migration module. Its ports are built only when the <c>coordination</c> section selects
    /// PostgreSQL. Opens no connection. A second call fails with <c>hosting.registration_duplicate</c>.
    /// </summary>
    public static IServiceCollection AddIskenderPostgreSqlCoordination(this IServiceCollection services)
    {
        CoordinatorRegistration.Add(services, new CompositionModule(CoordinationModule, [], ["coordination", PersistenceModule]), new CoordinatorRegistration(
            CoordinatorKind.PostgreSql,
            CoordinatorCapabilities.PostgreSql,
            health: provider => new PostgreSqlCoordinationHealth(Units(provider), Settings(provider), Logger(provider, CoordinationTelemetry.SourceName), provider.GetService<TimeProvider>()),
            atomic: provider => new PostgreSqlAtomicOperations(Units(provider), Settings(provider), Logger(provider, CoordinationTelemetry.SourceName), provider.GetService<TimeProvider>()),
            leases: provider => new PostgreSqlLeaseCoordinator(Units(provider), Settings(provider), Logger(provider, CoordinationTelemetry.SourceName), provider.GetService<TimeProvider>()),
            fencing: provider => new PostgreSqlFencingGuard(Logger(provider, CoordinationTelemetry.SourceName), provider.GetService<TimeProvider>()),
            cache: provider => new PostgreSqlVersionedCache(Units(provider), Settings(provider), Logger(provider, CoordinationTelemetry.SourceName), provider.GetService<TimeProvider>())));
        services.AddSingleton(CoordinationMigrations.Module);

        return services;
    }

    private static IUnitOfWorkFactory Units(IServiceProvider provider) => provider.GetRequiredService<IUnitOfWorkFactory>();

    private static CoordinationSettings Settings(IServiceProvider provider) => provider.GetRequiredService<CoordinationSettings>();

    private static ILogger? Logger(IServiceProvider provider, string category) => provider.GetService<ILoggerFactory>()?.CreateLogger(category);
}
