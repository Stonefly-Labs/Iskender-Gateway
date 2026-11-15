using System.Collections.Concurrent;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Fixtures;

/// <summary>
/// A lease coordinator that presents its leases as PostgreSQL-issued, so the single-instance guard takes and holds them,
/// and records each acquisition and release in a shared journal. With <see cref="Add"/> it is the leases of a test
/// PostgreSQL coordinator registration.
/// </summary>
internal sealed class RecordingLeases(ConcurrentQueue<string> journal) : ILeaseCoordinator
{
    private int _releases;

    public int Releases => Volatile.Read(ref _releases);

    /// <summary>Registers these leases as a test PostgreSQL coordinator, under the module <c>fake-postgresql</c>.</summary>
    public void Add(IServiceCollection services) =>
        CoordinatorRegistration.Add(
            services,
            new CompositionModule("fake-postgresql", [], ["coordination"]),
            new CoordinatorRegistration(CoordinatorKind.PostgreSql, CoordinatorCapabilities.PostgreSql, _ => new HealthyProbe(), leases: _ => this));

    public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
    {
        journal.Enqueue($"acquire {name.Name}");
        return ValueTask.FromResult(LeaseAcquisition.Acquired(Grant(scope, name, duration)));
    }

    public ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Grant(lease.Scope, lease.Name, duration));

    public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _releases);
        journal.Enqueue($"release {lease.Name.Name}");
        return ValueTask.CompletedTask;
    }

    private static Lease Grant(CoordinationScope scope, CoordinationName name, TimeSpan duration) =>
        Lease.Grant(scope, name, new FencingToken(1), Guid.NewGuid(), CoordinatorKind.PostgreSql, duration, TimeProvider.System.GetTimestamp(), TimeSpan.Zero, TimeProvider.System);

    private sealed class HealthyProbe : ICoordinationHealthProbe
    {
        public ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(CoordinationHealthResult.Healthy);
    }
}
