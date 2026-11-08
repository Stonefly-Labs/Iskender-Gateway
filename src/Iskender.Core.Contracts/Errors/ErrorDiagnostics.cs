using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// Internal detail about an error, held apart from everything a client sees. The envelope codec never reads it, and
/// <see cref="ToString"/> returns <c>[withheld]</c>, so formatting it shows no content (FR-023). Filtering its content
/// before logging belongs to Telemetry/Audit (ISK-0071).
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class ErrorDiagnostics
{
    public ErrorDiagnostics(string? text = null, IEnumerable<KeyValuePair<string, string>>? values = null)
    {
        Text = string.IsNullOrEmpty(text) ? null : text;
        Values = Array.AsReadOnly(values?.ToArray() ?? []);
    }

    /// <summary>Free text, or null.</summary>
    public string? Text { get; }

    /// <summary>Key-value pairs, in the order given.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Values { get; }

    /// <summary>True when there is neither text nor a value.</summary>
    public bool IsEmpty => Text is null && Values.Count == 0;

    public override string ToString() => "[withheld]";
}
