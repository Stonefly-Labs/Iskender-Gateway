using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Redis;

/// <summary>Adds the Redis coordinator to a host's service collection (hosting contract, Registration rule 6; research R4). Owned by ISK-0006.</summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>The module <see cref="AddIskenderRedisCoordination"/> composes.</summary>
    public const string Module = "coordination-redis";

    /// <summary>
    /// Composes module <c>coordination-redis</c>, which requires <c>coordination</c>: one <see cref="RedisCoordinator"/>
    /// per container, built from the <c>coordination</c> section's <c>redis</c> fields with the host's
    /// <see cref="ICoordinationCredentialProvider"/> if it registered one, and a <see cref="CoordinatorRegistration"/> of
    /// kind <c>redis</c> exposing its ports and health probe. The coordinator connects on its first call, so registering
    /// and resolving it opens no connection. A second call fails with <c>hosting.registration_duplicate</c>.
    /// </summary>
    public static IServiceCollection AddIskenderRedisCoordination(this IServiceCollection services)
    {
        CoordinatorRegistration.Add(services, new CompositionModule(Module, [], ["coordination"]), new CoordinatorRegistration(
            CoordinatorKind.Redis,
            RedisCoordinator.Capabilities,
            health: provider => Coordinator(provider).Health,
            atomic: provider => Coordinator(provider).Atomic,
            leases: provider => Coordinator(provider).Leases,
            cache: provider => Coordinator(provider).Cache));
        services.AddSingleton(provider => new RedisCoordinator(
            provider.GetRequiredService<CoordinationSettings>(),
            provider.GetService<ICoordinationCredentialProvider>(),
            provider.GetService<ILoggerFactory>()?.CreateLogger(CoordinationTelemetry.SourceName),
            provider.GetService<TimeProvider>()));

        return services;
    }

    private static RedisCoordinator Coordinator(IServiceProvider provider) => provider.GetRequiredService<RedisCoordinator>();
}
