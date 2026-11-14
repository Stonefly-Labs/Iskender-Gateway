using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Fixtures;

/// <summary>
/// Builds Iskender's services from their own descriptors, once each, passing itself to their factories, so every
/// service Iskender code resolves, then or later, is recorded. Anything else comes from the real container.
/// </summary>
internal sealed class RecordingProvider(IServiceProvider real, IReadOnlyList<ServiceDescriptor> iskender) : IServiceProvider
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
