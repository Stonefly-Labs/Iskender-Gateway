using System.Globalization;
using System.Text.Json;

namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>One entry of <c>break-record.json</c>. A missing or mistyped field reads as null, so H2 can name it.</summary>
public sealed record BreakEntry(string? Contract, string? Item, string? Kind, string? Reason, string? ApprovedBy, int? Version, string? Date)
{
    public override string ToString() => $"{Contract} {Item} {Kind}";
}

/// <summary>A break found in this change, as an entry must name it.</summary>
public sealed record FoundBreak(string Contract, string Item, string Kind);

/// <summary>The H2 result: its violations, and the found breaks that a new entry declares, as notices.</summary>
public sealed record BreakRecordCheck(IReadOnlyList<Violation> Violations, IReadOnlyList<Notice> Declared)
{
    public bool IsDeclared(FoundBreak found) =>
        Declared.Any(notice => notice.RuleId == found.Kind && notice.Contract == found.Contract && notice.Item == found.Item);
}

/// <summary>
/// Loads the break record and checks it with H2. An entry present at the base is history and must stay exactly as it was;
/// every other entry is new, and must be complete, appear once, name the Architecture (ADR) owner, give the version the
/// break lands in, and match a break in this change.
/// </summary>
public static class BreakRecord
{
    public const string Approver = "Architecture (ADR) owner";

    /// <summary>Reads the record's entries. A missing file has none.</summary>
    public static IReadOnlyList<BreakEntry> Parse(byte[]? json)
    {
        if (json is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("entries").EnumerateArray().Select(entry => new BreakEntry(
            Text(entry, "contract"),
            Text(entry, "item"),
            Text(entry, "kind"),
            Text(entry, "reason"),
            Text(entry, "approvedBy"),
            entry.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) ? number : null,
            Text(entry, "date"))).ToList();
    }

    /// <summary>
    /// Checks the record. <paramref name="landsIn"/> gives the contract version an entry's break lands in, or null when it is
    /// unknown, such as for a package that was removed; an entry's <c>version</c> must equal it.
    /// </summary>
    public static BreakRecordCheck Check(
        IReadOnlyList<BreakEntry> baseEntries, IReadOnlyList<BreakEntry> current, IReadOnlyList<FoundBreak> breaks, string baseName, Func<BreakEntry, int?>? landsIn = null)
    {
        var violations = new List<Violation>();
        var declared = new List<Notice>();

        void Report(BreakEntry entry, string what) =>
            violations.Add(new Violation("H2", entry.Contract ?? "break-record", entry.Version, $"entry {entry}", what, RuleCatalog.BreakRecordPath, baseName));

        // Pair each base entry with an identical current one; whatever is left on either side was edited, removed, or added.
        var added = current.ToList();
        var unmatched = new List<BreakEntry>();
        foreach (var entry in baseEntries)
        {
            var index = added.IndexOf(entry);
            if (index >= 0)
            {
                added.RemoveAt(index);
            }
            else
            {
                unmatched.Add(entry);
            }
        }

        foreach (var entry in unmatched)
        {
            var edited = added.FirstOrDefault(candidate => candidate.Contract == entry.Contract && candidate.Item == entry.Item && candidate.Kind == entry.Kind);
            if (edited is not null)
            {
                added.Remove(edited);
                Report(entry, "historical entry edited");
            }
            else
            {
                Report(entry, "historical entry removed");
            }
        }

        var seen = new List<BreakEntry>();
        foreach (var entry in added)
        {
            var missing = Missing(entry);
            if (missing.Count > 0)
            {
                Report(entry, $"incomplete: {string.Join(", ", missing)} missing");
                continue;
            }

            if (baseEntries.Contains(entry))
            {
                Report(entry, "repeats a historical entry");
            }
            else if (seen.Contains(entry))
            {
                Report(entry, "repeats another new entry");
            }
            else if (!RuleCatalog.IsBreakKind(entry.Kind!))
            {
                Report(entry, $"kind {entry.Kind} is not a break kind");
            }
            else if (entry.ApprovedBy != Approver)
            {
                Report(entry, $"approvedBy is \"{entry.ApprovedBy}\", not \"{Approver}\"");
            }
            else if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                Report(entry, $"date \"{entry.Date}\" is not yyyy-MM-dd");
            }
            else if (landsIn?.Invoke(entry) is { } version && entry.Version != version)
            {
                Report(entry, $"version is {entry.Version}, but the break lands in {entry.Contract}@{version.ToString(CultureInfo.InvariantCulture)}");
            }
            else if (!breaks.Contains(new FoundBreak(entry.Contract!, entry.Item!, entry.Kind!)))
            {
                Report(entry, "matches no break in this change");
            }
            else
            {
                declared.Add(new Notice("declared", entry.Kind!, entry.Contract!, entry.Item!, entry.Reason!));
            }

            seen.Add(entry);
        }

        return new BreakRecordCheck(violations, declared);
    }

    private static List<string> Missing(BreakEntry entry)
    {
        var missing = new List<string>();
        Add(entry.Contract, "contract");
        Add(entry.Item, "item");
        Add(entry.Kind, "kind");
        Add(entry.Reason, "reason");
        Add(entry.ApprovedBy, "approvedBy");
        if (entry.Version is not > 0)
        {
            missing.Add("version");
        }

        Add(entry.Date, "date");
        return missing;

        void Add(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(name);
            }
        }
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
