using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.Container;

/// <summary>
/// A serving variant a plane's definition adds, such as the control plane's <c>work</c>: the same serve path as no
/// arguments, with its own plane, <c>service.name</c>, and registrations, and only the health listener (ISK-0009 research
/// R11, container contract Command line rule 1). It takes no further arguments. <see cref="Check"/>, when set, resolves
/// the settings its modules check across fields, after validation and before anything binds, so a configuration they
/// reject exits 2 as validation does (maintainer decision, 2026-11-18, <c>implement</c>). Owned by ISK-0009.
/// </summary>
public sealed record ContainerServingCommand(
    CompositionPlane Plane,
    string ServiceName,
    Action<IServiceCollection, CompositionSources> Compose,
    Action<IServiceProvider>? Check = null);
