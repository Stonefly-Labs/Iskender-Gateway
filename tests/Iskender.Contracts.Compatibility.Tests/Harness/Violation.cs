using System.Globalization;

namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>
/// One failure, in the contract's Violation format. <see cref="Appendix"/> carries extra lines, such as H3's regenerated
/// baseline, printed after the message.
/// </summary>
public sealed record Violation(
    string RuleId,
    string Contract,
    int? Version,
    string Item,
    string WhatChanged,
    string ComparedWith,
    string Base,
    string? Appendix = null)
{
    public string Message
    {
        get
        {
            var rule = RuleCatalog.Get(RuleId);
            var version = Version?.ToString(CultureInfo.InvariantCulture) ?? "?";
            var line = $"{RuleId}: {Contract}@{version} {Item}: {WhatChanged}. Compared with {ComparedWith} at {Base}. Fix: {rule.Fix} "
                + $"See {RuleCatalog.ContractPath}#{rule.Section} (version {RuleCatalog.ContractVersion}).";
            return Appendix is null ? line : line + "\n" + Appendix;
        }
    }

    public override string ToString() => Message;

    /// <summary>Sorts by catalog order, then contract, then item, with ordinal comparison, and drops exact duplicates.</summary>
    public static IReadOnlyList<Violation> Sort(IEnumerable<Violation> violations) =>
        violations
            .Distinct()
            .OrderBy(violation => IndexOf(violation.RuleId))
            .ThenBy(violation => violation.Contract, StringComparer.Ordinal)
            .ThenBy(violation => violation.Item, StringComparer.Ordinal)
            .ThenBy(violation => violation.WhatChanged, StringComparer.Ordinal)
            .ToList();

    private static int IndexOf(string ruleId)
    {
        for (var index = 0; index < RuleCatalog.Order.Count; index++)
        {
            if (string.Equals(RuleCatalog.Order[index], ruleId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}

/// <summary>A declared break or a deprecation: reported by every run, even one that passes.</summary>
public sealed record Notice(string Kind, string RuleId, string Contract, string Item, string Detail)
{
    public override string ToString() => $"{Kind}: {RuleId} {Contract} {Item}: {Detail}";
}
