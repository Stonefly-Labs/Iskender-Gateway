using Iskender.Domain.Ports;
using Iskender.Infrastructure.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Coordination;

/// <summary>Adds ISK-0005's coordination to a host's service collection (hosting contract, Registration rule 6). Owned by ISK-0006.</summary>
public static class CoordinationServiceCollectionExtensions
{
    /// <summary>
    /// Composes module <c>coordination</c>, which requires <c>hosting</c>: its settings, the local substitutes, and the
    /// ports, guard, capability report, and gate that <see cref="CoordinationStartup"/> composes at host start from the
    /// local substitutes and any <see cref="CoordinatorRegistration"/>. Every service is a container singleton. Reads no
    /// configuration and opens no connection. A second call fails with <c>hosting.registration_duplicate</c>.
    /// </summary>
    public static IServiceCollection AddIskenderCoordination(this IServiceCollection services)
    {
        CompositionRegistration.Add(services, new CompositionModule(SingleInstanceGuard.Module, [CoordinationSectionSchema.Schema], ["hosting"]));

        services.AddSingleton(provider => CoordinationSettings.From(provider.GetRequiredService<IValidatedConfiguration>().Snapshot));
        services.AddSingleton(provider => new LocalAtomicOperations(provider.GetRequiredService<CoordinationSettings>(), provider.GetService<TimeProvider>()));
        services.AddSingleton(provider => new LocalLeaseCoordinator(provider.GetRequiredService<CoordinationSettings>(), Logger(provider), provider.GetService<TimeProvider>()));
        services.AddSingleton(provider => new LocalVersionedCache(provider.GetRequiredService<CoordinationSettings>(), Logger(provider), provider.GetService<TimeProvider>()));
        services.AddSingleton<ComposedCoordination>();
        services.AddSingleton(provider => provider.GetRequiredService<ComposedCoordination>().Report);
        services.AddSingleton(provider => provider.GetRequiredService<ComposedCoordination>().Gate);
        services.AddSingleton<ISingleInstanceStatus>(provider => provider.GetRequiredService<ComposedCoordination>().Guard);
        services.AddSingleton(provider => provider.GetRequiredService<ComposedCoordination>().Atomic);
        services.AddSingleton(provider => provider.GetRequiredService<ComposedCoordination>().Leases);
        services.AddSingleton(provider => provider.GetRequiredService<ComposedCoordination>().Fencing);
        services.AddSingleton(provider => provider.GetRequiredService<ComposedCoordination>().Cache);
        services.AddSingleton<ICoordinationHealthProbe>(provider =>
        {
            var settings = provider.GetRequiredService<CoordinationSettings>();
            var registrations = provider.GetServices<CoordinatorRegistration>().ToList();
            return new CoordinationHealthProbe(
                settings,
                postgreSqlConfigured: registrations.Any(registration => registration.Kind == CoordinatorKind.PostgreSql),
                HealthOf(CoordinatorKind.Redis),
                HealthOf(CoordinatorKind.PostgreSql),
                Logger(provider),
                provider.GetService<TimeProvider>());

            // A probe is built only for a coordinator the configuration selects, so an unused one is never constructed.
            ICoordinationHealthProbe? HealthOf(CoordinatorKind kind) =>
                CoordinationPrimitives.All.Any(primitive => settings.CoordinatorFor(primitive) == kind)
                    ? registrations.FirstOrDefault(registration => registration.Kind == kind)?.Health(provider)
                    : null;
        });
        services.AddSingleton<ICompositionStartup>(provider => new CoordinationStartup(
            provider.GetRequiredService<ComposedCoordination>(),
            provider,
            provider.GetServices<CoordinatorRegistration>(),
            provider.GetServices<FeatureRequirement>(),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>()));

        return services;
    }

    private static ILogger? Logger(IServiceProvider provider) => provider.GetService<ILoggerFactory>()?.CreateLogger(CoordinationTelemetry.SourceName);
}
