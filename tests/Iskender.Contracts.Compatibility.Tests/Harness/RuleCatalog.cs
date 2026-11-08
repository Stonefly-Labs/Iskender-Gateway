namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>One rule of the published contract: its ID, family, contract section, whether it is a break kind, and its fix.</summary>
public sealed record Rule(string Id, char Family, string Section, bool IsBreakKind, string Fix);

/// <summary>
/// Every rule of <c>contracts/public-contracts-and-versioning.md</c> (version 1), in the contract's order: the Break
/// catalog tables, then Other rules. Rule H4 checks that the contract's tables list exactly these, in this order.
/// </summary>
public static class RuleCatalog
{
    public const string ContractPath = "specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md";
    public const int ContractVersion = 1;
    public const string BreakRecordPath = "tests/Iskender.Contracts.Compatibility.Tests/break-record.json";

    private const string BreakFix = "restore compatibility, or declare the break in " + BreakRecordPath + ".";

    public static IReadOnlyList<Rule> All { get; } =
    [
        Break("S1"), Break("S2"), Break("S3"), Break("S4"), Break("S5"), Break("S6"), Break("S7"), Break("S8"), Break("S9"),
        Break("W1"), Break("W2"), Break("W3"), Break("W4"), Break("W5"), Break("W6"),
        Break("G1"), Break("G2"), Break("G3"), Break("G4"), Break("G5"),
        Other("V1", "add [ContractVersion(\"<contract>\", <version>)] naming the package's contract and the version that introduced the item."),
        Other("V2", "set [assembly: ContractPackage] to the base version plus one when the surface changed, or leave it at the base version when it did not."),
        Other("V3", "restore the tag as it was at the base; a published tag never changes."),
        Other("V4", "use [Obsolete(\"Deprecated in <contract>@<n>. Use <replacement>.\")], or end with \"No replacement.\" when there is none."),
        Other("H1", "add the missing input, or run git fetch origin main."),
        Other("H2", "complete the entry, set approvedBy to Architecture (ADR) owner, match a break in this change, and leave entries from the base unchanged."),
        Other("H3", "replace the committed baseline with the regenerated text printed below."),
        Other("H4", "make the contract's table match the source."),
        Other("H5", "declare the instance in ErrorCodes or ResourceTypes."),
        Other("H6", "remove the derivation, or add the type to the H6 table through a revision of the contract."),
        Other("H7", "remove the member, or use a type outside the denied namespaces and names."),
        Other("H8", "add a [ProvesRule] test that fails with the rule's ID on a negative fixture."),
    ];

    public static IReadOnlyList<string> Order { get; } = All.Select(rule => rule.Id).ToArray();

    /// <summary>The types H6 allows to derive from <c>OwnershipResolver</c>, by full name. The contract's H6 table lists them; it is empty.</summary>
    public static IReadOnlyList<string> OwnershipResolverAllowList { get; } = [];

    public static Rule Get(string id) =>
        All.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Rule '{id}' is not in the catalog.", nameof(id));

    public static bool IsBreakKind(string id) => All.Any(rule => rule.IsBreakKind && string.Equals(rule.Id, id, StringComparison.Ordinal));

    private static Rule Break(string id) => new(id, id[0], "break-catalog", IsBreakKind: true, BreakFix);

    private static Rule Other(string id, string fix) => new(id, id[0], "other-rules", IsBreakKind: false, fix);
}
