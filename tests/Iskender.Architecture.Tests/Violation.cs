using System.Globalization;

namespace Iskender.Architecture.Tests;

/// <summary>
/// One broken rule. Identity is rule, source, and target, so one bad edge reached by two checks of the same
/// rule is reported once.
/// </summary>
public sealed record Violation(string RuleId, string Source, string Target, string Detail, string ContractPath, int? ContractVersion)
{
    public string Message =>
        $"{RuleId}: {Source} -> {Target}: {Detail}. See {ContractPath} (version {ContractVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}).";

    public bool Equals(Violation? other) =>
        other is not null
        && string.Equals(RuleId, other.RuleId, StringComparison.Ordinal)
        && string.Equals(Source, other.Source, StringComparison.Ordinal)
        && string.Equals(Target, other.Target, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(RuleId),
            StringComparer.Ordinal.GetHashCode(Source),
            StringComparer.Ordinal.GetHashCode(Target));

    public override string ToString() => Message;

    /// <summary>
    /// Collapses duplicates and sorts by rule order in the source, then source, then target. Comparison is
    /// ordinal, so that Windows and Linux report identically. A rule the order does not list sorts last.
    /// </summary>
    public static IReadOnlyList<Violation> Normalize(IEnumerable<Violation> violations, IReadOnlyList<string> ruleOrder)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < ruleOrder.Count; index++)
        {
            positions.TryAdd(ruleOrder[index], index);
        }

        return violations
            .Distinct()
            .OrderBy(violation => positions.TryGetValue(violation.RuleId, out var position) ? position : int.MaxValue)
            .ThenBy(violation => violation.RuleId, StringComparer.Ordinal)
            .ThenBy(violation => violation.Source, StringComparer.Ordinal)
            .ThenBy(violation => violation.Target, StringComparer.Ordinal)
            .ToList();
    }
}
