namespace Iskender.Domain.Ports;

/// <summary>One module's migration state against this release. Owned by ISK-0004.</summary>
public sealed class MigrationStatus
{
    public MigrationStatus(
        string module,
        int? schemaVersion,
        SchemaVersionRange supportedRange,
        IEnumerable<MigrationIdentity> pending,
        IEnumerable<MigrationIdentity> unknown,
        IEnumerable<MigrationIdentity> checksumMismatches)
    {
        Module = PortNames.RequireModule(module);
        SchemaVersion = schemaVersion;
        SupportedRange = supportedRange;
        Pending = [.. pending];
        Unknown = [.. unknown];
        ChecksumMismatches = [.. checksumMismatches];
    }

    public string Module { get; }

    /// <summary>The highest applied version, or <c>null</c> when none is applied.</summary>
    public int? SchemaVersion { get; }

    public SchemaVersionRange SupportedRange { get; }

    /// <summary>Known migrations not yet applied.</summary>
    public IReadOnlyList<MigrationIdentity> Pending { get; }

    /// <summary>Applied migrations this release does not know.</summary>
    public IReadOnlyList<MigrationIdentity> Unknown { get; }

    /// <summary>Applied migrations whose recorded checksum differs from the shipped scripts.</summary>
    public IReadOnlyList<MigrationIdentity> ChecksumMismatches { get; }
}
