namespace Iskender.Domain.Ports;

/// <summary>The migrations a run applied or reversed, in the order it changed them. Owned by ISK-0004.</summary>
public sealed class MigrationRunResult
{
    public MigrationRunResult(IEnumerable<MigrationIdentity> applied, IEnumerable<MigrationIdentity> reversed)
    {
        Applied = [.. applied];
        Reversed = [.. reversed];
    }

    public IReadOnlyList<MigrationIdentity> Applied { get; }

    public IReadOnlyList<MigrationIdentity> Reversed { get; }
}
