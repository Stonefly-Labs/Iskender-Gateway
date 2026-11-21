using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Management;

/// <summary>The worker's composition root (hosting contract, Ownership rule 2; ISK-0009 research R11). Owned by ISK-0009.</summary>
public static class WorkerServiceCollectionExtensions
{
    /// <summary>The worker root's module, which registers no configuration section.</summary>
    public const string WorkerModule = "worker";

    /// <summary>
    /// Registers the worker's composition root: its configuration, validated once per container at host start, and the
    /// composition lifecycle, through the same core as <c>AddIskenderControlPlane(...)</c>. Runs
    /// <paramref name="configure"/> once against fresh options and copies the result. Reads no document and runs no
    /// validation. <c>null</c> arguments fail with <c>core.value.invalid</c>, and a second composition root on one
    /// collection fails with <c>hosting.registration_duplicate</c> before changing anything.
    /// </summary>
    public static IServiceCollection AddIskenderWorker(this IServiceCollection services, Action<WorkerOptions> configure)
    {
        if (services is null || configure is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        CompositionServices.ThrowIfRootRegistered(services, WorkerModule);

        var options = new WorkerOptions();
        configure(options);

        CompositionServices.AddRoot(
            services,
            new CompositionRoot(CompositionPlane.Worker, WorkerModule),
            new CompositionSources(options.BaseDocument, options.OverlayDocument, options.ReadEnvironmentVariables, options.EnvironmentName),
            []);
        return services;
    }
}
