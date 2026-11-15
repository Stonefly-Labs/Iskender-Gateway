using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting.Configuration;
using Iskender.Hosting.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting;

/// <summary>Adds Iskender to a host's own service collection (hosting contract, Registration). Owned by ISK-0006.</summary>
[ContractVersion("hosting", 1)]
public static class IskenderServiceCollectionExtensions
{
    /// <summary>The module <c>AddIskender(...)</c> registers, which <c>MapIskender(...)</c> requires.</summary>
    internal const string HostingModule = "hosting";

    /// <summary>
    /// Registers Iskender's configuration, validated once per container at host start, its start-up, and each request's
    /// context, which resolves only inside an Iskender request (<c>core.context.not_resolved</c> otherwise). Runs
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

        CompositionServices.ThrowIfRootRegistered(services, HostingModule);

        var options = new IskenderOptions();
        configure(options);

        CompositionServices.AddRoot(
            services,
            new CompositionRoot(CompositionPlane.DataPlane, HostingModule),
            Sources(options.BaseDocument, options.OverlayDocument, options.ReadEnvironmentVariables, options.EnvironmentName),
            [HostingSectionSchema.Schema]);
        services.AddSingleton(provider => HostingSettings.From(provider.GetRequiredService<ConfigurationSnapshot>()));
        services.AddScoped<RequestContextHolder>();
        services.AddScoped(provider => provider.GetRequiredService<RequestContextHolder>().Required);

        return new IskenderBuilder(services);
    }

    /// <summary>The composition sources for the documents and settings <c>AddIskender(...)</c> copied from its options.</summary>
    internal static CompositionSources Sources(
        IskenderConfigurationDocument? baseDocument,
        IskenderConfigurationDocument? overlayDocument,
        bool readEnvironmentVariables,
        string? environmentName) =>
        new(baseDocument?.Composition, overlayDocument?.Composition, readEnvironmentVariables, environmentName);
}
