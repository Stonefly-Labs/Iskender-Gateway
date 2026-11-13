using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance;

/// <summary>
/// A protected resource written through a fence (contract Leases and fencing rule 6): the PostgreSQL fencing guard with
/// a fixture row, or <see cref="RecordingFence"/> for implementations tested without PostgreSQL.
/// </summary>
public interface IFenceUnderTest
{
    /// <summary>
    /// Writes <paramref name="resource"/> under <paramref name="lease"/>'s token. Returns <c>true</c> when the write
    /// committed, and <c>false</c> when it was refused with <c>coordination.fencing_rejected</c> and nothing committed.
    /// </summary>
    ValueTask<bool> WriteAsync(CoordinationName resource, Lease lease, CancellationToken cancellationToken);

    /// <summary>The tokens whose writes committed for <paramref name="resource"/> in <paramref name="scope"/>, in commit order.</summary>
    ValueTask<IReadOnlyList<long>> CommittedTokensAsync(CoordinationScope scope, CoordinationName resource, CancellationToken cancellationToken);
}
