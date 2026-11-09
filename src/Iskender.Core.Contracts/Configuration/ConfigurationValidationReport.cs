using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// The result of validating a configuration: a snapshot and its revision, or at least one error and no snapshot
/// (FR-022). There is no way to build a failure that holds a snapshot, or a success that holds an error.
/// </summary>
[ContractVersion("core-contracts", 2)]
public sealed class ConfigurationValidationReport
{
    internal const int MaxReportedErrors = 100;

    private ConfigurationValidationReport(ConfigurationSnapshot? snapshot, ImmutableArray<ConfigurationFieldError> errors, int totalErrorCount)
    {
        Snapshot = snapshot;
        Errors = errors;
        TotalErrorCount = totalErrorCount;
    }

    /// <summary>True exactly when there is a snapshot and no error.</summary>
    public bool IsValid => Snapshot is not null;

    /// <summary>The validated snapshot, set exactly when valid.</summary>
    public ConfigurationSnapshot? Snapshot { get; }

    /// <summary>The snapshot's revision, set exactly when valid.</summary>
    public ConfigurationRevision? Revision => Snapshot?.Revision;

    /// <summary>The reported errors: empty when valid, and 1 to 100 when not.</summary>
    public IReadOnlyList<ConfigurationFieldError> Errors { get; }

    /// <summary>Every error found: 0 when valid, and at least <see cref="Errors"/>' count when not.</summary>
    public int TotalErrorCount { get; }

    /// <summary>A valid report. Fails with <c>core.value.invalid</c> for a null snapshot.</summary>
    public static ConfigurationValidationReport Success(ConfigurationSnapshot snapshot) =>
        new(snapshot ?? throw new GatewayException(ErrorCodes.ValueInvalid), [], 0);

    /// <summary>
    /// A rejected report. Fails with <c>core.value.invalid</c> for no errors, a null error, more than 100 errors, or a
    /// total below the number of errors.
    /// </summary>
    public static ConfigurationValidationReport Failure(IEnumerable<ConfigurationFieldError> errors, int totalErrorCount)
    {
        var list = errors?.ToImmutableArray() ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        if (list.IsEmpty || list.Length > MaxReportedErrors || list.Any(error => error is null) || totalErrorCount < list.Length)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new(null, list, totalErrorCount);
    }

    /// <summary>The text rendering: one line per error, a truncation line when errors were not shown, and a final line.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var error in Errors)
        {
            text.Append(error).Append('\n');
        }

        if (TotalErrorCount > Errors.Count)
        {
            text.Append(CultureInfo.InvariantCulture, $"... {TotalErrorCount - Errors.Count} more error(s) not shown.\n");
        }

        text.Append(IsValid
            ? $"Configuration valid: revision {Revision}."
            : string.Create(CultureInfo.InvariantCulture, $"{TotalErrorCount} configuration error(s); configuration rejected."));
        return text.ToString();
    }
}
