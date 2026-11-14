using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.Redis.Tests.Composition;

/// <summary>The Redis registration's module, requirement, repeat rule, and lazy connection, without a container (research R4).</summary>
public class RedisRegistrationTests
{
    private const string Document = """
        {"iskender":{"coordination":{"atomic":{"coordinator":"redis"},"leases":{"coordinator":"redis"},"cache":{"coordinator":"redis"},
        "redis":{"endpoints":["127.0.0.1:1"],"username":"gateway","password":{"secretRef":"test/redis"},"tlsMode":"disable"}}}}
        """;

    [Fact]
    public void ItComposesItsModuleRequiringCoordination()
    {
        var services = new ServiceCollection();

        services.AddIskenderRedisCoordination();

        var module = Assert.IsType<CompositionModule>(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CompositionModule)).ImplementationInstance);
        Assert.Equal("coordination-redis", module.Name);
        Assert.Empty(module.Sections);
        Assert.Equal(["coordination"], module.Requires);
        var registration = Assert.IsType<CoordinatorRegistration>(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CoordinatorRegistration)).ImplementationInstance);
        Assert.Equal(CoordinatorKind.Redis, registration.Kind);
        Assert.Equal(CoordinatorCapabilities.Redis, registration.Capabilities);
        Assert.Null(registration.Fencing);
    }

    [Fact]
    public void ARepeatFailsBeforeAnythingChanges()
    {
        var services = new ServiceCollection();
        services.AddIskenderRedisCoordination();
        var before = services.ToList();

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderRedisCoordination()).Code);
        Assert.Equal(before, services);
    }

    [Fact]
    public async Task ResolvingThePortsOpensNoConnectionBeforeFirstUse()
    {
        var credentials = new TestCoordinationCredentialProvider(new Dictionary<string, string>(StringComparer.Ordinal) { ["test/redis"] = "unused" });
        var services = new ServiceCollection();
        services.AddSingleton(TestCoordinationSettings.Read(Document));
        services.AddSingleton<ICoordinationCredentialProvider>(credentials);
        services.AddIskenderRedisCoordination();
        var registration = (CoordinatorRegistration)Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CoordinatorRegistration)).ImplementationInstance!;
        var provider = new DescriptorProvider(services);

        var atomic = registration.Atomic!(provider);
        var leases = registration.Leases!(provider);
        var cache = registration.Cache!(provider);
        var health = registration.Health(provider);

        var coordinator = provider.GetRequiredService<RedisCoordinator>();
        Assert.Same(coordinator.Atomic, atomic);
        Assert.Same(coordinator.Leases, leases);
        Assert.Same(coordinator.Cache, cache);
        Assert.Same(coordinator.Health, health);
        Assert.Equal(0, credentials.Calls);
        await coordinator.DisposeAsync();
    }

    // Resolves singletons from the collection's own descriptors, once each, as a container would.
    private sealed class DescriptorProvider(IServiceCollection services) : IServiceProvider
    {
        private readonly Dictionary<Type, object?> _built = [];

        public object? GetService(Type serviceType)
        {
            if (_built.TryGetValue(serviceType, out var built))
            {
                return built;
            }

            var descriptor = services.LastOrDefault(candidate => candidate.ServiceType == serviceType);
            built = descriptor?.ImplementationInstance ?? descriptor?.ImplementationFactory?.Invoke(this);
            _built[serviceType] = built;
            return built;
        }
    }
}
