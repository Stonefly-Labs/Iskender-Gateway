using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.Composition;

/// <summary>
/// How a module registration adds its <see cref="CompositionModule"/> to a host's service collection (hosting contract,
/// Registration rules 7 and 9). It forwards to the composition core's <see cref="CompositionServices.AddModule"/>, which
/// the composition root builds the schema registry and checks requirements from. Owned by ISK-0006.
/// </summary>
public static class CompositionRegistration
{
    /// <summary>
    /// Adds <paramref name="module"/> as a container singleton, and the composition check once per collection. A module of
    /// the same name already in the collection fails with <c>hosting.registration_duplicate</c> before anything changes,
    /// and <c>null</c> arguments fail with <c>core.value.invalid</c>.
    /// </summary>
    public static void Add(IServiceCollection services, CompositionModule module) => CompositionServices.AddModule(services, module);
}
