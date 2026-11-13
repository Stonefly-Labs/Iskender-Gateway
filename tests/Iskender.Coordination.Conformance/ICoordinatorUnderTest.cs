using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance;

/// <summary>
/// The seam each implementation's test project fills so the conformance suite can run against it (FR-029). A
/// distributed implementation gives independent clients, standing in for replicas, from <see cref="CreateClient"/>.
/// </summary>
public interface ICoordinatorUnderTest
{
    CoordinatorKind Kind { get; }

    /// <summary>The levels the implementation declares, which the suite holds it to.</summary>
    CoordinatorCapabilities Capabilities { get; }

    /// <summary>Whether clients share state across processes, so scenarios run from two independent clients.</summary>
    bool IsDistributed { get; }

    /// <summary>The bounds the implementation was configured with.</summary>
    ConformanceSettings Settings { get; }

    /// <summary>Scopes for two organizations and one project in each.</summary>
    ConformanceScopes Scopes { get; }

    /// <summary>Skips or fails the calling test when the coordinator cannot run, such as without a container runtime.</summary>
    void Require();

    /// <summary>
    /// Client <paramref name="index"/>, from 0. For a distributed implementation each index is an independent connection
    /// or data source; for a local one every index shares the process's state.
    /// </summary>
    CoordinatorClient CreateClient(int index);

    /// <summary>
    /// Lets <paramref name="duration"/> pass on the coordinator's clock: advances a fake clock for a local substitute, or
    /// waits in real time for a distributed one.
    /// </summary>
    ValueTask PassTimeAsync(TimeSpan duration);

    /// <summary>Whether <see cref="StopAsync"/>, <see cref="PauseAsync"/>, and <see cref="ResumeAsync"/> are supported.</summary>
    bool SupportsFaults => false;

    /// <summary>Stops the coordinator. Supported only when <see cref="SupportsFaults"/>.</summary>
    ValueTask StopAsync() => throw new NotSupportedException();

    /// <summary>Pauses the coordinator, so requests hang. Supported only when <see cref="SupportsFaults"/>.</summary>
    ValueTask PauseAsync() => throw new NotSupportedException();

    /// <summary>Resumes or restarts the coordinator. Supported only when <see cref="SupportsFaults"/>.</summary>
    ValueTask ResumeAsync() => throw new NotSupportedException();
}

/// <summary>One client's ports, standing in for one replica.</summary>
public sealed record CoordinatorClient(IAtomicOperations Atomic, ILeaseCoordinator Leases, IVersionedCache Cache);

/// <summary>The configured bounds the suite checks against, mirroring the <c>coordination</c> section's fields.</summary>
public sealed record ConformanceSettings(
    TimeSpan MinimumLeaseDuration,
    TimeSpan MaximumLeaseDuration,
    TimeSpan SafetyMargin,
    TimeSpan MaximumWait,
    TimeSpan MaximumExpiry,
    int MaximumValueBytes,
    TimeSpan OperationTimeout);
