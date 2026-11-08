using Iskender.Contracts.Compatibility.Tests.Harness;

namespace Iskender.Contracts.Compatibility.Tests;

public class ViolationTests
{
    private const string Item = "method Iskender.Core.Contracts.Identifiers.ResourceId::Parse(System.String,Iskender.Core.Contracts.Identifiers.ResourceType)";

    [Fact]
    public void ABreakKindUsesTheContractsExampleFormat()
    {
        var violation = new Violation("S2", "core-contracts", 1, Item, "member removed", "baselines/core-contracts.surface.txt", "3f2a9c1");

        Assert.Equal(
            "S2: core-contracts@1 " + Item + ": member removed. Compared with baselines/core-contracts.surface.txt at 3f2a9c1. "
                + "Fix: restore compatibility, or declare the break in tests/Iskender.Contracts.Compatibility.Tests/break-record.json. "
                + "See specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md#break-catalog (version 1).",
            violation.Message);
    }

    [Fact]
    public void AnOtherRuleNamesItsOwnFixAndSection()
    {
        var violation = new Violation("H1", "policy-sdk", 1, "baselines/policy-sdk.surface.txt", "baseline missing", "the checkout", "3f2a9c1");

        Assert.StartsWith("H1: policy-sdk@1 baselines/policy-sdk.surface.txt: baseline missing. Compared with the checkout at 3f2a9c1. Fix: add the missing input", violation.Message, StringComparison.Ordinal);
        Assert.EndsWith("public-contracts-and-versioning.md#other-rules (version 1).", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAppendixFollowsTheMessageOnItsOwnLines()
    {
        var violation = new Violation("H3", "core-contracts", 1, "baselines/core-contracts.surface.txt", "stale", "the current surface", "abc", "line one\nline two");

        Assert.EndsWith("(version 1).\nline one\nline two", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ViolationsSortByCatalogOrderThenContractThenItemOrdinally()
    {
        Violation V(string rule, string contract, string item) => new(rule, contract, 1, item, "x", "y", "z");

        var sorted = Violation.Sort(
        [
            V("H3", "core-contracts", "a"),
            V("V2", "core-contracts", "a"),
            V("S2", "policy-sdk", "a"),
            V("S2", "core-contracts", "b"),
            V("S2", "core-contracts", "B"),
            V("S10", "core-contracts", "a"),
            V("S1", "core-contracts", "z"),
            V("S1", "core-contracts", "z"),
        ]);

        Assert.Equal(
            ["S1 z", "S2 B", "S2 b", "S2 a", "V2 a", "H3 a"],
            sorted.Where(violation => violation.RuleId != "S10").Select(violation => $"{violation.RuleId} {violation.Item}"));
        Assert.Equal("policy-sdk", sorted[3].Contract);
        Assert.Equal("S10", sorted[^1].RuleId);
    }

    [Fact]
    public void TheCatalogListsTheThirtyTwoRulesInContractOrder()
    {
        Assert.Equal(
            ["S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9", "W1", "W2", "W3", "W4", "W5", "W6", "G1", "G2", "G3", "G4", "G5",
             "V1", "V2", "V3", "V4", "H1", "H2", "H3", "H4", "H5", "H6", "H7", "H8"],
            RuleCatalog.Order);
        Assert.All(RuleCatalog.All, rule => Assert.Equal(rule.Family is 'S' or 'W' or 'G', rule.IsBreakKind));
    }
}
