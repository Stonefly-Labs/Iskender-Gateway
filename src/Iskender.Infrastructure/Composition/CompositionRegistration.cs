using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.Composition;

/// <summary>
/// How a module registration adds its <see cref="CompositionModule"/> to a host's service collection (hosting contract,
/// Registration rule 7). Hosting builds the schema registry and checks requirements from every module registered here.
/// Owned by ISK-0006.
/// </summary>
public static class CompositionRegistration
{
    /// <summary>
    /// Adds <paramref name="module"/> as a container singleton, and <see cref="CompositionCheck"/> once per collection. A
    /// module of the same name already in the collection fails with <c>hosting.registration_duplicate</c> before anything
    /// changes, and <c>null</c> arguments fail with <c>core.value.invalid</c>.
    /// </summary>
    public static void Add(IServiceCollection services, CompositionModule module)
    {
        if (services is null || module is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (Modules(services).Any(registered => registered.Name == module.Name))
        {
            throw new GatewayException(GatewayError.Create(
                ErrorCodes.HostingRegistrationDuplicate,
                RequestCorrelationId.New(),
                OutputVisibility.None,
                diagnostics: new ErrorDiagnostics($"{ErrorCodes.HostingRegistrationDuplicate.Id}: module {module.Name}", [new("module", module.Name)])));
        }

        services.AddSingleton(module);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(CompositionCheck)))
        {
            services.AddSingleton(provider => new CompositionCheck(
                provider.GetServices<CompositionModule>(),
                (provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger(CompositionCheck.Category)));
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<CompositionCheck>());
        }
    }

    private static IEnumerable<CompositionModule> Modules(IServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(CompositionModule) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CompositionModule>();
}
