namespace Iskender.Core.Contracts.Versioning;

/// <summary>A serialized format with its own version, and the versions a reader accepts.</summary>
[ContractVersion("core-contracts", 1)]
public sealed class WireContract
{
    internal WireContract(string id, int currentVersion, params int[] supportedVersions)
    {
        Id = id;
        CurrentVersion = currentVersion;
        SupportedVersions = Array.AsReadOnly(supportedVersions.Order().ToArray());
    }

    /// <summary>The wire contract ID, such as <c>error-envelope</c>.</summary>
    public string Id { get; }

    /// <summary>The version a writer produces.</summary>
    public int CurrentVersion { get; }

    /// <summary>Every version a reader accepts, in ascending order.</summary>
    public IReadOnlyList<int> SupportedVersions { get; }
}
