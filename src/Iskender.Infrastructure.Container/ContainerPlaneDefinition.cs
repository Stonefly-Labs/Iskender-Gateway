using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.Container;

/// <summary>
/// What one container entry point serves: its plane, its OpenTelemetry <c>service.name</c>, the registrations it composes
/// from the resolved sources, the routes of its traffic listener, and the commands it adds (data-model "Entry points").
/// A definition with no <see cref="MapTraffic"/> binds only its health listener. Owned by ISK-0007.
/// </summary>
public sealed record ContainerPlaneDefinition(
    CompositionPlane Plane,
    string ServiceName,
    Action<IServiceCollection, CompositionSources> Compose,
    Action<WebApplication>? MapTraffic,
    IReadOnlyDictionary<string, ContainerCommand> Commands)
{
    /// <summary><c>data_plane</c>, <c>control_plane</c>, or <c>worker</c>, as events 7001, 7004, and 7005 name it.</summary>
    public string PlaneName => NameOf(Plane);

    /// <summary>The name events 7001, 7004, and 7005 give <paramref name="plane"/>.</summary>
    internal static string NameOf(CompositionPlane plane) => plane switch
    {
        CompositionPlane.DataPlane => "data_plane",
        CompositionPlane.ControlPlane => "control_plane",
        CompositionPlane.Worker => "worker",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
