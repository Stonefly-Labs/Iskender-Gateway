using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Hosting.Composition;
using Iskender.Hosting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting;

/// <summary>Adds Iskender to a host's own service collection (hosting contract, Registration). Owned by ISK-0006.</summary>
[ContractVersion("hosting", 1)]
public static class IskenderServiceCollectionExtensions
{
    /// <summary>
    /// Registers Iskender's configuration, validated once per container at host start, and its start-up. Runs
    /// <paramref name="configure"/> once against fresh options and copies the result. Reads no document and runs no
    /// validation. <c>null</c> arguments fail with <c>core.value.invalid</c>, and a second call on one collection fails with
    /// <c>hosting.registration_duplicate</c> before changing anything.
    /// </summary>
    public static IskenderBuilder AddIskender(this IServiceCollection services, Action<IskenderOptions> configure)
    {
        if (services is null || configure is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        CompositionGuard.ThrowIfRegistered(services, CompositionGuard.HostingModule);

        var options = new IskenderOptions();
        configure(options);

        services.AddSingleton(IskenderRuntimeOptions.From(options));
        services.AddSingleton(new CompositionModule(CompositionGuard.HostingModule, [HostingSectionSchema.Schema], []));
        services.AddSingleton(provider => ConfigurationSchemaRegistry.Create(provider.GetServices<CompositionModule>().SelectMany(module => module.Sections)));
        services.AddSingleton(provider => new IskenderConfiguration(
            provider.GetRequiredService<IskenderRuntimeOptions>(),
            provider.GetRequiredService<ConfigurationSchemaRegistry>(),
            provider.GetService<ILoggerFactory>()));
        services.AddSingleton<IValidatedConfiguration>(provider => provider.GetRequiredService<IskenderConfiguration>());
        services.AddSingleton(provider => provider.GetRequiredService<IskenderConfiguration>().Snapshot);
        services.AddSingleton(provider => provider.GetRequiredService<ConfigurationSnapshot>().Revision);
        services.AddSingleton(provider => HostingSettings.From(provider.GetRequiredService<ConfigurationSnapshot>()));
        services.AddSingleton(provider => new IskenderStartup(
            provider.GetServices<CompositionModule>(),
            provider.GetRequiredService<IskenderConfiguration>(),
            provider.GetServices<ICompositionStartup>,
            provider.GetService<ILoggerFactory>()));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<IskenderStartup>());
        services.AddSingleton<IHostedLifecycleService>(provider => provider.GetRequiredService<IskenderStartup>());

        return new IskenderBuilder(services);
    }
}
