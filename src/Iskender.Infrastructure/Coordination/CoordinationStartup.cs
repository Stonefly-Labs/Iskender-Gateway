using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// Composes the container's coordination at host start, after validation (hosting contract, Start-up rule 6; research
/// R10): it checks that every coordinator the configuration selects is registered, builds each primitive's port, starts
/// the data plane's single-instance guard, runs the capability check once, and builds the gate. At host stop it disposes
/// the guard, which stops its timer and releases its lease. Owned by ISK-0006.
/// </summary>
internal sealed class CoordinationStartup : ICompositionStartup
{
    /// <summary>The <c>{RequiredBy}</c> of event 6003 for a coordinator the configuration selects.</summary>
    public const string RequiredByConfiguration = "configuration";

    private readonly ComposedCoordination _composed;
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<CoordinatorRegistration> _registrations;
    private readonly IReadOnlyList<FeatureRequirement> _requirements;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    /// <param name="services">The container the registrations' factories and the local substitutes are resolved from.</param>
    public CoordinationStartup(
        ComposedCoordination composed,
        IServiceProvider services,
        IEnumerable<CoordinatorRegistration> registrations,
        IEnumerable<FeatureRequirement> requirements,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null)
    {
        if (composed is null || services is null || registrations is null || requirements is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        _composed = composed;
        _services = services;
        _registrations = [.. registrations];
        _requirements = [.. requirements];
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string ModuleName => SingleInstanceGuard.Module;

    public async ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var settings = _services.GetRequiredService<CoordinationSettings>();
        foreach (var primitive in CoordinationPrimitives.All)
        {
            var kind = settings.CoordinatorFor(primitive);
            if (kind != CoordinatorKind.Local && !Offers(Find(kind), primitive))
            {
                throw CompositionLog.RegistrationMissing(_loggerFactory.CreateLogger(CompositionLog.Category), CompositionLog.CoordinatorMissing(kind, primitive), RequiredByConfiguration);
            }
        }

        var atomic = Build(settings.AtomicCoordinator, registration => registration.Atomic!, services => services.GetRequiredService<LocalAtomicOperations>());
        var leases = Build(settings.LeaseCoordinator, registration => registration.Leases!, services => services.GetRequiredService<LocalLeaseCoordinator>());
        var cache = Build(settings.CacheCoordinator, registration => registration.Cache!, services => services.GetRequiredService<LocalVersionedCache>());
        var fencing = Find(settings.LeaseCoordinator)?.Fencing?.Invoke(_services);

        var logger = _loggerFactory.CreateLogger(CoordinationTelemetry.SourceName);
        var guard = new SingleInstanceGuard(leases, settings.LeaseCoordinator, SingleInstancePlane.DataPlane, settings.InstanceLeaseDuration, logger, _timeProvider);
        try
        {
            var singleInstance = SingleInstanceState.NotApplicable;
            if (settings.Topology == CoordinationTopology.SingleInstance)
            {
                await guard.StartAsync(cancellationToken).ConfigureAwait(false);
                singleInstance = guard.State;
            }

            var postgreSqlConfigured = Find(CoordinatorKind.PostgreSql) is not null;
            var capabilities = CoordinationPrimitives.All.ToDictionary(
                primitive => primitive,
                primitive => settings.IsConfigured(primitive, postgreSqlConfigured) ? CapabilitiesOf(settings.CoordinatorFor(primitive)) : null);
            var report = CoordinationCapabilityChecker.Check(_requirements, settings.Topology, capabilities, singleInstance);
            CoordinationLog.CapabilityReport(logger, report);

            _composed.Complete(atomic, leases, fencing, cache, guard, new CoordinationGate(report, atomic, leases, cache, fencing, guard));
        }
        catch
        {
            await guard.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_composed.IsComposed)
        {
            await _composed.Guard.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Fencing follows the lease coordinator, so it needs only the registration; one without a fencing guard offers none.
    private static bool Offers(CoordinatorRegistration? registration, CoordinationPrimitive primitive) => registration is not null && primitive switch
    {
        CoordinationPrimitive.AtomicOperations => registration.Atomic is not null,
        CoordinationPrimitive.Leases => registration.Leases is not null,
        CoordinationPrimitive.CacheInvalidation => registration.Cache is not null,
        _ => true,
    };

    private CoordinatorRegistration? Find(CoordinatorKind kind) => _registrations.FirstOrDefault(registration => registration.Kind == kind);

    private T Build<T>(CoordinatorKind kind, Func<CoordinatorRegistration, Func<IServiceProvider, T>> factory, Func<IServiceProvider, T> local) =>
        kind == CoordinatorKind.Local ? local(_services) : factory(Find(kind)!)(_services);

    private CoordinatorCapabilities CapabilitiesOf(CoordinatorKind kind) =>
        kind == CoordinatorKind.Local ? CoordinatorCapabilities.Local : Find(kind)!.Capabilities;
}
