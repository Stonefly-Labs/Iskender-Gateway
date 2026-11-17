using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Management;

/// <summary>The control plane's composition root (hosting contract, Ownership rule 2; research R3). Owned by ISK-0007.</summary>
public static class ControlPlaneServiceCollectionExtensions
{
    /// <summary>The control-plane root's module, which registers no configuration section.</summary>
    public const string ControlPlaneModule = "control-plane";

    /// <summary>
    /// Registers the control plane's composition root: its configuration, validated once per container at host start,
    /// and the composition lifecycle. Runs <paramref name="configure"/> once against fresh options and copies the result.
    /// Reads no document and runs no validation. <c>null</c> arguments fail with <c>core.value.invalid</c>, and a second
    /// composition root on one collection, <c>AddIskender(...)</c> included, fails with
    /// <c>hosting.registration_duplicate</c> before changing anything.
    /// </summary>
    public static IServiceCollection AddIskenderControlPlane(this IServiceCollection services, Action<ControlPlaneOptions> configure)
    {
        if (services is null || configure is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        CompositionServices.ThrowIfRootRegistered(services, ControlPlaneModule);

        var options = new ControlPlaneOptions();
        configure(options);

        CompositionServices.AddRoot(
            services,
            new CompositionRoot(CompositionPlane.ControlPlane, ControlPlaneModule),
            new CompositionSources(options.BaseDocument, options.OverlayDocument, options.ReadEnvironmentVariables, options.EnvironmentName),
            []);
        return services;
    }
}
