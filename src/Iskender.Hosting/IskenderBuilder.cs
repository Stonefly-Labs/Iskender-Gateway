using Iskender.Core.Contracts.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting;

/// <summary>What <c>AddIskender(...)</c> returns: the host's own service collection, for later Iskender registrations.</summary>
[ContractVersion("hosting", 1)]
public sealed class IskenderBuilder
{
    internal IskenderBuilder(IServiceCollection services) => Services = services;

    /// <summary>The host's service collection that Iskender was added to.</summary>
    public IServiceCollection Services { get; }
}
