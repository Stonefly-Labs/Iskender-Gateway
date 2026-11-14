using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Registries;
using Iskender.Contracts.Compatibility.Tests.Support;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>H4: the published contract's tables match their sources, and its rule tables match the rule catalog (FR-001, FR-002, US5 scenario 2).</summary>
public class ContractDocumentRuleTests
{
    private static readonly DirectoryFileSet Checkout = new(Repository.Root(), "the checkout");

    private static readonly ContractSources Sources = new(
        RegistrySnapshot.Current(),
        BreakRecord.Parse(Checkout.Read(RuleCatalog.BreakRecordPath)),
        [.. new[] { "Iskender.Core.Contracts", "Iskender.Policy.Sdk", "Iskender.Providers.Abstractions", "Iskender.Hosting" }.Select(project => HarnessRun.LoadReferencedPackage(project)!)],
        RuleCatalog.OwnershipResolverAllowList);

    private static List<string> Contract() => [.. Checkout.ReadText(RuleCatalog.ContractPath)!.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];

    private static Violation Only(List<string> lines)
    {
        var violation = Assert.Single(ContractDocumentRules.Check(lines, Sources, "the checkout"));
        Assert.Equal("H4", violation.RuleId);
        return violation;
    }

    private static int IndexOf(List<string> lines, string start) => lines.FindIndex(line => line.StartsWith(start, StringComparison.Ordinal));

    [Fact]
    public void ThePublishedContractMatchesItsSources()
    {
        Assert.Empty(ContractDocumentRules.Check(Checkout, Sources));
    }

    [Fact]
    [ProvesRule("H4")]
    public void AChangedSafeMessageFailsH4()
    {
        var lines = Contract();
        var index = IndexOf(lines, "| `core.cancelled` |");
        lines[index] = lines[index].Replace("The request was cancelled. |", "The request was stopped. |", StringComparison.Ordinal);

        var violation = Only(lines);
        Assert.Equal("Error codes table, row core.cancelled", violation.Item);
        Assert.Equal("safe message is \"The request was stopped.\", the source has \"The request was cancelled.\"", violation.WhatChanged);
    }

    [Fact]
    [ProvesRule("H4")]
    public void AMissingCodeRowFailsH4()
    {
        var lines = Contract();
        lines.RemoveAt(IndexOf(lines, "| `core.internal` |"));

        var violation = Only(lines);
        Assert.Equal(("Error codes table, row core.internal", "row missing"), (violation.Item, violation.WhatChanged));
    }

    [Fact]
    [ProvesRule("H4")]
    public void AnExtraRuleRowFailsH4()
    {
        var lines = Contract();
        lines.Insert(IndexOf(lines, "| H8 |") + 1, "| H9 | A rule the harness does not have | FR-002 |");

        var violation = Only(lines);
        Assert.Equal(("Rule table, row H9", "rule is not in the harness's rule catalog"), (violation.Item, violation.WhatChanged));
    }

    [Fact]
    [ProvesRule("H4")]
    public void AReorderedRuleFailsH4()
    {
        var lines = Contract();
        var v1 = IndexOf(lines, "| V1 |");
        (lines[v1], lines[v1 + 1]) = (lines[v1 + 1], lines[v1]);

        var violation = Only(lines);
        Assert.Equal(("Rule table, row V2", "rule is out of order: position 21 should be V1, as in the rule catalog"), (violation.Item, violation.WhatChanged));
    }

    [Fact]
    [ProvesRule("H4")]
    public void AnUnmappedRetryProseFailsH4()
    {
        var lines = Contract();
        var index = IndexOf(lines, "| `cancelled` |");
        lines[index] = lines[index].Replace("| No |", "| Never |", StringComparison.Ordinal);

        Assert.Equal("retry rule is \"unmapped: Never\", the source has \"Never\"", Only(lines).WhatChanged);
    }

    [Fact]
    [ProvesRule("H4")]
    public void AWrongContractVersionFailsH4()
    {
        var lines = Contract();
        var index = IndexOf(lines, "   | `Iskender.Policy.Sdk` |");
        lines[index] = lines[index].Replace("| 1 |", "| 2 |", StringComparison.Ordinal);

        var violation = Only(lines);
        Assert.Equal(("Contract versions table, row Iskender.Policy.Sdk", "version is \"2\", the source has \"1\""), (violation.Item, violation.WhatChanged));
    }

    [Fact]
    [ProvesRule("H4")]
    public void ABreakRecordEntryMissingFromTheTableFailsH4()
    {
        var entry = new BreakEntry("core-contracts", "type X", "S1", "Test.", "Architecture (ADR) owner", 2, "2026-11-20");

        // The entry follows any the repository has already declared, so this holds whether the real record is empty or not.
        var violation = Assert.Single(ContractDocumentRules.Check(Contract(), Sources with { BreakRecord = [.. Sources.BreakRecord, entry] }, "the checkout"));
        Assert.Equal($"Break record table, row {Sources.BreakRecord.Count + 1}", violation.Item);
    }

    [Fact]
    [ProvesRule("H4")]
    public void AnUnlistedOwnershipResolverAllowanceFailsH4()
    {
        var violation = Assert.Single(ContractDocumentRules.Check(Contract(), Sources with { OwnershipResolverAllowList = ["Iskender.Domain.Resolver"] }, "the checkout"));
        Assert.Equal(("H6 table, row Iskender.Domain.Resolver", "row missing"), (violation.Item, violation.WhatChanged));
    }
}
