using Iskender.Domain.Ports;
using Iskender.Infrastructure.Composition;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Health;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Work;
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

    /// <summary>The module <see cref="AddIskenderPostgreSqlWork"/> composes.</summary>
    public const string WorkModule = "work-postgresql";

    /// <summary>The module <see cref="AddIskenderPostgreSqlWorkDispatcher"/> composes.</summary>
    public const string WorkDispatcherModule = WorkDispatcherStartup.Module;

    /// <summary>The coordination feature the dispatcher reaches its leases and fencing guard through.</summary>
    public const string WorkDispatchFeature = "work.dispatch";

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
    /// <c>coordination</c> migration module and the <c>directory</c> one its writes register scopes in (persistence
    /// contract Migrations rule 12, ISK-0009); <c>coordination</c> declares no dependency on <c>directory</c>. Its ports
    /// are built only when the <c>coordination</c> section selects PostgreSQL. Opens no connection. A second call fails
    /// with <c>hosting.registration_duplicate</c>.
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
        AddDirectoryMigrations(services);

        return services;
    }

    /// <summary>
    /// Composes module <c>work-postgresql</c>, which requires <c>persistence-postgresql</c> and has no section (<c>work</c>
    /// contract Registration rule 1, ISK-0009): the <see cref="IWorkOutbox"/>, over every
    /// <see cref="WorkKindDeclaration"/> the host registered, and the <c>directory</c> and <c>work</c> migration modules.
    /// It declares no <see cref="FeatureRequirement"/>, because the outbox uses only the caller's unit. Opens no
    /// connection. A second call fails with <c>hosting.registration_duplicate</c>.
    /// </summary>
    public static IServiceCollection AddIskenderPostgreSqlWork(this IServiceCollection services)
    {
        CompositionRegistration.Add(services, new CompositionModule(WorkModule, [], [PersistenceModule]));

        services.AddSingleton<IWorkOutbox>(provider => new PostgreSqlWorkOutbox(provider.GetServices<WorkKindDeclaration>(), Logger(provider, WorkLog.Category)));
        AddDirectoryMigrations(services);
        services.AddSingleton(WorkMigrations.Module);

        return services;
    }

    /// <summary>
    /// Composes module <c>work-dispatcher</c>, which requires <c>work-postgresql</c> and <c>coordination-postgresql</c>
    /// (<c>work</c> contract Registration rule 2, Leases and fencing rule 1, ISK-0009): the <c>work</c> section and its
    /// settings, the feature <c>work.dispatch</c> needing strict leases and fencing on a mandatory path that fails closed,
    /// and the dispatcher's composition start-up. The dispatcher reaches leases and the fencing guard only through
    /// <c>CoordinationGate.ForFeature("work.dispatch")</c>, and resolves each kind's handler from the container. It is
    /// embeddable, under the same requirements. Opens no connection until the start-up runs. A second call fails with
    /// <c>hosting.registration_duplicate</c>.
    /// </summary>
    public static IServiceCollection AddIskenderPostgreSqlWorkDispatcher(this IServiceCollection services)
    {
        CompositionRegistration.Add(services, new CompositionModule(WorkDispatcherModule, [WorkSectionSchema.Schema], [WorkModule, CoordinationModule]));

        services.AddSingleton(new FeatureRequirement(
            WorkDispatchFeature,
            WorkDispatcher.Module,
            new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = ConsistencyLevel.Strict, [CoordinationPrimitive.Fencing] = ConsistencyLevel.Strict },
            mandatoryPath: true,
            OutageBehavior.FailClosed));
        services.AddSingleton(provider => WorkSettings.From(provider.GetRequiredService<IValidatedConfiguration>().Snapshot, Settings(provider)));
        services.AddSingleton<ICompositionStartup>(provider => new WorkDispatcherStartup(
            () => Dispatcher(provider),
            Logger(provider, WorkLog.Category),
            () => provider.GetService<CapabilityReport>()?.Find(WorkDispatchFeature) is { Accepted: true }));

        return services;
    }

    // Built when the start-up runs, after the coordination start-up has composed the gate.
    private static WorkDispatcher Dispatcher(IServiceProvider provider)
    {
        var settings = provider.GetRequiredService<WorkSettings>();
        var feature = provider.GetRequiredService<CoordinationGate>().ForFeature(WorkDispatchFeature);
        return new WorkDispatcher(
            Units(provider),
            feature.Fencing,
            new ShardLeases(feature.Leases, settings.LeaseDuration, provider.GetService<TimeProvider>()),
            settings,
            provider.GetServices<WorkHandlerRegistration>(),
            type => (IWorkHandler)provider.GetRequiredService(type),
            provider.GetService<IWorkInjection>(),
            Logger(provider, WorkLog.Category),
            provider.GetService<TimeProvider>());
    }

    private static IUnitOfWorkFactory Units(IServiceProvider provider) => provider.GetRequiredService<IUnitOfWorkFactory>();

    private static CoordinationSettings Settings(IServiceProvider provider) => provider.GetRequiredService<CoordinationSettings>();

    private static ILogger? Logger(IServiceProvider provider, string category) => provider.GetService<ILoggerFactory>()?.CreateLogger(category);

    // Both the coordinator and the outbox need the directory module, and a migration set refuses it twice.
    private static void AddDirectoryMigrations(IServiceCollection services)
    {
        if (!services.Any(descriptor => !descriptor.IsKeyedService && ReferenceEquals(descriptor.ImplementationInstance, DirectoryMigrations.Module)))
        {
            services.AddSingleton(DirectoryMigrations.Module);
        }
    }
}
