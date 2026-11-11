using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance;

/// <summary>
/// An in-memory fence that applies contract Leases and fencing rule 6 atomically: a write commits only when its token is
/// at least the highest committed for the resource and comes from the lease key the resource was first fenced with. It
/// stands in for the PostgreSQL guard when an implementation is tested without PostgreSQL. Keys are per exact scope.
/// </summary>
public sealed class RecordingFence : IFenceUnderTest
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Record> _records = new(StringComparer.Ordinal);

    public ValueTask<bool> WriteAsync(CoordinationName resource, Lease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        var key = CoordinationKey.Hash(lease.Scope.Owner, resource.Module, resource.Name);
        var leaseKey = CoordinationKey.Hash(lease.Scope.Owner, lease.Name.Module, lease.Name.Name);
        lock (_gate)
        {
            if (!_records.TryGetValue(key, out var record))
            {
                _records[key] = record = new Record(leaseKey);
            }

            if (record.LeaseKey != leaseKey || (record.Committed.Count > 0 && lease.Token.Value < record.Committed[^1]))
            {
                return ValueTask.FromResult(false);
            }

            record.Committed.Add(lease.Token.Value);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<IReadOnlyList<long>> CommittedTokensAsync(CoordinationScope scope, CoordinationName resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(resource);
        var key = CoordinationKey.Hash(scope.Owner, resource.Module, resource.Name);
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<long>>(_records.TryGetValue(key, out var record) ? [.. record.Committed] : []);
        }
    }

    private sealed class Record(string leaseKey)
    {
        public string LeaseKey { get; } = leaseKey;

        public List<long> Committed { get; } = [];
    }
}
