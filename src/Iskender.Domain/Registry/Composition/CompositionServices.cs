using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Domain.Registry.Composition;

/// <summary>
/// The registrations both composition roots and every module make (hosting contract, Ownership rules 1, 2, and 5, and
/// Registration rules 7 to 9). Owned by ISK-0007.
/// </summary>
public static class CompositionServices
{
    /// <summary>
    /// Registers <paramref name="root"/>, its module with <paramref name="rootSections"/>, the schema registry, the
    /// validated configuration, its snapshot and revision, <see cref="CompositionStatus"/>, and
    /// <see cref="CompositionLifecycle"/> as a hosted lifecycle service. A second root on one collection fails with
    /// <c>hosting.registration_duplicate</c>, naming this root's module, before anything changes, and <c>null</c>
    /// arguments fail with <c>core.value.invalid</c>. Reads no document and runs no validation.
    /// </summary>
    public static void AddRoot(IServiceCollection services, CompositionRoot root, CompositionSources sources, IEnumerable<SectionSchema> rootSections)
    {
        if (services is null || root is null || sources is null || rootSections is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        ThrowIfRootRegistered(services, root.Module);
        var module = new CompositionModule(root.Module, rootSections, []);

        services.AddSingleton(root);
        services.AddSingleton(sources);
        services.AddSingleton(module);
        services.AddSingleton(provider => ConfigurationSchemaRegistry.Create(provider.GetServices<CompositionModule>().SelectMany(registered => registered.Sections)));
        services.AddSingleton(provider => new CompositionConfiguration(
            provider.GetRequiredService<CompositionSources>(),
            provider.GetRequiredService<ConfigurationSchemaRegistry>(),
            provider.GetService<ILoggerFactory>()));
        services.AddSingleton<IValidatedConfiguration>(provider => provider.GetRequiredService<CompositionConfiguration>());
        services.AddSingleton(provider => provider.GetRequiredService<CompositionConfiguration>().Snapshot);
        services.AddSingleton(provider => provider.GetRequiredService<ConfigurationSnapshot>().Revision);
        services.AddSingleton<CompositionStatus>();
        services.AddSingleton(provider => new CompositionLifecycle(
            provider.GetServices<CompositionModule>(),
            provider.GetRequiredService<CompositionConfiguration>(),
            provider.GetRequiredService<CompositionStatus>(),
            provider.GetServices<ICompositionStartup>,
            provider.GetService<ILoggerFactory>(),
            provider.GetService<CapabilityReport>));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<CompositionLifecycle>());
        services.AddSingleton<IHostedLifecycleService>(provider => provider.GetRequiredService<CompositionLifecycle>());
    }

    /// <summary>
    /// Fails with <c>hosting.registration_duplicate</c>, naming <paramref name="module"/>, when a composition root or a
    /// module of that name is already registered, so a root can check before it runs its caller's callback.
    /// </summary>
    public static void ThrowIfRootRegistered(IServiceCollection services, string module)
    {
        if (services is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (CompositionModules.Root(services) is not null)
        {
            throw CompositionModules.Duplicate(module);
        }

        CompositionModules.ThrowIfRegistered(services, module);
    }

    /// <summary>
    /// Adds <paramref name="module"/> as a container singleton, and <see cref="CompositionCheck"/> once per collection. A
    /// module of the same name already in the collection fails with <c>hosting.registration_duplicate</c> before anything
    /// changes, and <c>null</c> arguments fail with <c>core.value.invalid</c>.
    /// </summary>
    public static void AddModule(IServiceCollection services, CompositionModule module)
    {
        if (services is null || module is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (CompositionModules.Modules(services).Any(registered => registered.Name == module.Name))
        {
            throw CompositionModules.Duplicate(module.Name);
        }

        services.AddSingleton(module);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(CompositionCheck)))
        {
            services.AddSingleton(provider => new CompositionCheck(
                provider.GetServices<CompositionModule>(),
                provider.GetService<CompositionRoot>(),
                (provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger(CompositionCheck.Category)));
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<CompositionCheck>());
        }
    }
}
