using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The container's coordination, as <see cref="CoordinationStartup"/> composed it at host start (research R10): the
/// selected ports, the single-instance guard, the <see cref="CapabilityReport"/>, and the <see cref="CoordinationGate"/>.
/// Before start, and after a failed start, every accessor fails with <c>coordination.capability_unsupported</c>, so no
/// call passes a gate that was never checked. So does <see cref="Fencing"/> when the selected lease coordinator offers no
/// fencing guard. Disposing it disposes the guard and every disposable port it was given, and is safe twice. Owned by
/// ISK-0006.
/// </summary>
internal sealed class ComposedCoordination : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private Composition? _composition;
    private bool _disposed;

    public IAtomicOperations Atomic => Current.Atomic;

    public ILeaseCoordinator Leases => Current.Leases;

    public IFencingGuard Fencing => Current.Fencing ?? throw Unsupported();

    public IVersionedCache Cache => Current.Cache;

    public SingleInstanceGuard Guard => Current.Guard;

    public CapabilityReport Report => Current.Gate.Report;

    public CoordinationGate Gate => Current.Gate;

    /// <summary>True once <see cref="Complete"/> has run.</summary>
    public bool IsComposed
    {
        get
        {
            lock (_gate)
            {
                return _composition is not null;
            }
        }
    }

    private Composition Current
    {
        get
        {
            lock (_gate)
            {
                return _composition ?? throw Unsupported();
            }
        }
    }

    /// <summary>Records the start's result. Only the first call counts; a second fails with <c>core.value.invalid</c>.</summary>
    public void Complete(IAtomicOperations atomic, ILeaseCoordinator leases, IFencingGuard? fencing, IVersionedCache cache, SingleInstanceGuard guard, CoordinationGate gate)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_composition is not null)
            {
                throw new GatewayException(ErrorCodes.ValueInvalid);
            }

            _composition = new(atomic, leases, fencing, cache, guard, gate);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Composition? composition;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            composition = _composition;
        }

        if (composition is null)
        {
            return;
        }

        await composition.Guard.DisposeAsync().ConfigureAwait(false);
        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance) { composition.Guard };
        foreach (var port in new object?[] { composition.Atomic, composition.Leases, composition.Fencing, composition.Cache })
        {
            if (port is null || !disposed.Add(port))
            {
                continue;
            }

            if (port is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (port is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static GatewayException Unsupported() =>
        CoordinationFailures.Raise(ErrorCodes.CoordinationCapabilityUnsupported, CoordinationFailureClass.CapabilityUnsupported);

    private sealed record Composition(
        IAtomicOperations Atomic,
        ILeaseCoordinator Leases,
        IFencingGuard? Fencing,
        IVersionedCache Cache,
        SingleInstanceGuard Guard,
        CoordinationGate Gate);
}
