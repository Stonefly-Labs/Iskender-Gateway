namespace Iskender.Domain.Ports;

/// <summary>
/// Applies and reverses module migrations. It runs only when called: nothing runs it on start-up (FR-021), and it uses the
/// migration credentials (FR-022). Owned by ISK-0004.
/// </summary>
public interface IMigrationRunner
{
    /// <summary>Applies every pending migration in global order, each in its own transaction.</summary>
    ValueTask<MigrationRunResult> ApplyAsync(MigrationApplyRequest request, CancellationToken cancellationToken);

    /// <summary>Reverses the latest applied migrations in exactly the opposite order (FR-023).</summary>
    ValueTask<MigrationRunResult> ReverseAsync(MigrationReverseRequest request, CancellationToken cancellationToken);

    /// <summary>Each known module's schema version, supported range, and pending, unknown, and mismatched migrations.</summary>
    ValueTask<IReadOnlyList<MigrationStatus>> GetStatusAsync(CancellationToken cancellationToken);
}
