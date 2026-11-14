using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// A distributed coordinator an adapter registration offers to <c>AddIskenderCoordination()</c> (research R4): its kind,
/// its declared capabilities, and a factory for each primitive it offers and for its health probe. The factories run in
/// the host's container, only when the <c>coordination</c> section selects this kind, so registering one opens no
/// connection. A primitive whose factory is <c>null</c> is not offered. Owned by ISK-0006.
/// </summary>
public sealed class CoordinatorRegistration
{
    /// <summary>
    /// <paramref name="kind"/> is <c>redis</c> or <c>postgresql</c>, since the local substitutes are composed by
    /// <c>AddIskenderCoordination()</c> itself, and <paramref name="capabilities"/> must be declared for that kind. Bad
    /// input fails with <c>core.value.invalid</c>.
    /// </summary>
    public CoordinatorRegistration(
        CoordinatorKind kind,
        CoordinatorCapabilities capabilities,
        Func<IServiceProvider, ICoordinationHealthProbe> health,
        Func<IServiceProvider, IAtomicOperations>? atomic = null,
        Func<IServiceProvider, ILeaseCoordinator>? leases = null,
        Func<IServiceProvider, IFencingGuard>? fencing = null,
        Func<IServiceProvider, IVersionedCache>? cache = null)
    {
        if (!Enum.IsDefined(kind) || kind == CoordinatorKind.Local || capabilities is null || capabilities.Kind != kind || health is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Kind = kind;
        Capabilities = capabilities;
        Health = health;
        Atomic = atomic;
        Leases = leases;
        Fencing = fencing;
        Cache = cache;
    }

    public CoordinatorKind Kind { get; }

    public CoordinatorCapabilities Capabilities { get; }

    public Func<IServiceProvider, ICoordinationHealthProbe> Health { get; }

    public Func<IServiceProvider, IAtomicOperations>? Atomic { get; }

    public Func<IServiceProvider, ILeaseCoordinator>? Leases { get; }

    /// <summary>The fencing guard for this kind's leases, or <c>null</c> when it issues tokens nothing checks.</summary>
    public Func<IServiceProvider, IFencingGuard>? Fencing { get; }

    public Func<IServiceProvider, IVersionedCache>? Cache { get; }

    /// <summary>
    /// Adds <paramref name="registration"/> as a container singleton. A registration of the same kind already in the
    /// collection fails with <c>hosting.registration_duplicate</c> before anything changes, and <c>null</c> arguments
    /// fail with <c>core.value.invalid</c>.
    /// </summary>
    public static void Add(IServiceCollection services, CoordinatorRegistration registration)
    {
        if (services is null || registration is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (services.Any(descriptor => descriptor.ServiceType == typeof(CoordinatorRegistration) && !descriptor.IsKeyedService
            && descriptor.ImplementationInstance is CoordinatorRegistration { } registered && registered.Kind == registration.Kind))
        {
            var kind = registration.Kind.ToCode();
            throw new GatewayException(GatewayError.Create(
                ErrorCodes.HostingRegistrationDuplicate,
                RequestCorrelationId.New(),
                OutputVisibility.None,
                diagnostics: new ErrorDiagnostics($"{ErrorCodes.HostingRegistrationDuplicate.Id}: coordinator {kind}", [new("coordinator", kind)])));
        }

        services.AddSingleton(registration);
    }
}
