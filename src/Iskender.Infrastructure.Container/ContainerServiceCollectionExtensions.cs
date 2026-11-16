using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Composition;
using Iskender.Infrastructure.Container.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iskender.Infrastructure.Container;

/// <summary>Adds the container module to a container entry point's service collection (hosting contract, Registration rule 6). Owned by ISK-0007.</summary>
public static class ContainerServiceCollectionExtensions
{
    /// <summary>The module <see cref="AddIskenderContainer"/> composes.</summary>
    public const string Module = "container";

    /// <summary>
    /// Composes module <c>container</c>, which requires <c>hosting</c>: the <c>container</c> section's settings and the
    /// container lifecycle, each a container singleton. Only Iskender's own entry points call it (hosting contract,
    /// Coexistence rule 5). A second call fails with <c>hosting.registration_duplicate</c>, and a <c>null</c> collection
    /// with <c>core.value.invalid</c>.
    /// </summary>
    public static IServiceCollection AddIskenderContainer(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        CompositionRegistration.Add(services, new CompositionModule(Module, [ContainerSectionSchema.Schema], ["hosting"]));
        services.AddSingleton(provider => ContainerSettings.From(provider.GetRequiredService<IValidatedConfiguration>().Snapshot.GetSection(ContainerSectionSchema.Schema)));
        services.AddSingleton(provider => new ContainerLifecycle(provider.GetService<CompositionStatus>()));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<ContainerLifecycle>());
        services.AddSingleton<IHostedLifecycleService>(provider => provider.GetRequiredService<ContainerLifecycle>());
        return services;
    }
}
