using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Composition;

/// <summary>
/// Iskender never resolves the host's configuration or environment, and registers none of the services a host owns
/// (FR-002, research R6, hosting contract Registration rules 3 and 8).
/// </summary>
public class HostServiceIsolationTests
{
    private const string Document = """
        {"iskender":{"persistence":{"host":"iskender-database.invalid","database":"iskender","username":"gateway","password":{"secretRef":"database/runtime"}}}}
        """;

    private static readonly Type[] HostServices = [typeof(IConfiguration), typeof(IHostEnvironment), typeof(IWebHostEnvironment)];

    private static readonly Type[] HostOwned = [typeof(TimeProvider), typeof(ILoggerFactory), typeof(IDatabaseCredentialProvider), typeof(ICoordinationCredentialProvider)];

    [Fact]
    public async Task NoIskenderCodeResolvesTheHostsConfigurationOrEnvironmentDuringStartAndStop()
    {
        var services = HostLike();
        var iskender = Compose(services);
        await using var real = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var recorder = new RecordingProvider(real, iskender);
        var ct = TestContext.Current.CancellationToken;

        var hosted = recorder.GetServices<IHostedService>().ToList();
        foreach (var service in hosted.OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(ct);
        }

        foreach (var service in hosted)
        {
            await service.StartAsync(ct);
        }

        // Every Iskender service, once start has composed them. Local leases offer no fencing guard, so it fails closed, and
        // a request's context resolves only inside an Iskender request.
        foreach (var serviceType in iskender.Select(descriptor => descriptor.ServiceType).Distinct())
        {
            try
            {
                _ = recorder.GetService(serviceType);
            }
            catch (GatewayException unsupported) when (serviceType == typeof(IFencingGuard) && unsupported.Code == ErrorCodes.CoordinationCapabilityUnsupported)
            {
            }
            catch (GatewayException outside) when (serviceType == typeof(GatewayExecutionContext) && outside.Code == ErrorCodes.ContextNotResolved)
            {
            }
        }

        foreach (var service in Enumerable.Reverse(hosted))
        {
            await service.StopAsync(ct);
        }

        Assert.NotEmpty(recorder.Resolved);
        Assert.DoesNotContain(recorder.Resolved, HostServices.Contains);
    }

    [Fact]
    public void IskenderRegistersNoServiceAHostOwns()
    {
        var iskender = Compose(new ServiceCollection());

        Assert.DoesNotContain(iskender, descriptor => HostOwned.Contains(descriptor.ServiceType));
    }

    // A host's own registrations, so that a resolution of one would succeed and be recorded rather than fail.
    private static ServiceCollection HostLike()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new HostEnvironment());
        services.AddSingleton<IWebHostEnvironment>(new HostEnvironment());
        return services;
    }

    // Every Iskender registration, and the descriptors they added.
    private static List<ServiceDescriptor> Compose(ServiceCollection services)
    {
        var before = services.Count;
        services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, Document));
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderRedisCoordination();
        return [.. services.Skip(before)];
    }

    /// <summary>
    /// Builds Iskender's services from their own descriptors, once each, passing itself to their factories, so every
    /// service Iskender code resolves, then or later, is recorded. Anything else comes from the real container.
    /// </summary>
    private sealed class RecordingProvider(IServiceProvider real, IReadOnlyList<ServiceDescriptor> iskender) : IServiceProvider
    {
        private readonly Dictionary<ServiceDescriptor, object?> _built = [];
        private readonly List<Type> _resolved = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<Type> Resolved
        {
            get
            {
                lock (_gate)
                {
                    return [.. _resolved];
                }
            }
        }

        public object? GetService(Type serviceType)
        {
            lock (_gate)
            {
                _resolved.Add(serviceType);
            }

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var itemType = serviceType.GetGenericArguments()[0];
                var own = iskender.Where(descriptor => descriptor.ServiceType == itemType).ToList();
                if (own.Count > 0)
                {
                    var items = Array.CreateInstance(itemType, own.Count);
                    for (var index = 0; index < own.Count; index++)
                    {
                        items.SetValue(Build(own[index]), index);
                    }

                    return items;
                }
            }

            return iskender.LastOrDefault(descriptor => descriptor.ServiceType == serviceType) is { } descriptor ? Build(descriptor) : real.GetService(serviceType);
        }

        private object? Build(ServiceDescriptor descriptor)
        {
            lock (_gate)
            {
                if (_built.TryGetValue(descriptor, out var existing))
                {
                    return existing;
                }
            }

            var built = descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(this)
                ?? ActivatorUtilities.CreateInstance(this, descriptor.ImplementationType!);
            lock (_gate)
            {
                _built[descriptor] = built;
            }

            return built;
        }
    }

    private sealed class HostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";

        public string ApplicationName { get; set; } = "host";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
