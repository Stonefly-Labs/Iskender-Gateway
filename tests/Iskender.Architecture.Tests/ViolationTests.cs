namespace Iskender.Architecture.Tests;

public class ViolationTests
{
    private const string Path = "specs/0001/contracts/module-dependency-rules.md";

    private static Violation Create(string rule, string source, string target, string detail = "wrong") =>
        new(rule, source, target, detail, Path, 2);

    [Fact]
    public void MessageFollowsTheContractFormat()
    {
        var violation = Create("A5", "Iskender.Management", "Iskender.Infrastructure", "not an approved reference");

        Assert.Equal(
            "A5: Iskender.Management -> Iskender.Infrastructure: not an approved reference. See specs/0001/contracts/module-dependency-rules.md (version 2).",
            violation.Message);
    }

    [Fact]
    public void MessageSaysSoWhenTheVersionIsUnknown()
    {
        var violation = new Violation("C2", "rules.json", "file", "is missing", "rules.json", null);

        Assert.Equal("C2: rules.json -> file: is missing. See rules.json (version unknown).", violation.Message);
    }

    [Fact]
    public void DuplicatesCollapseOnRuleSourceAndTarget()
    {
        var violations = Violation.Normalize(
            [Create("A5", "a", "b", "first"), Create("A5", "a", "b", "second"), Create("A5", "a", "c"), Create("A3", "a", "b")],
            ["A3", "A5"]);

        Assert.Equal(["A3", "A5", "A5"], violations.Select(violation => violation.RuleId));
        Assert.Equal("first", violations[1].Detail);
    }

    [Fact]
    public void OrderIsRuleOrderThenSourceThenTarget()
    {
        var violations = Violation.Normalize(
            [Create("A5", "b", "a"), Create("A5", "a", "z"), Create("A5", "a", "b"), Create("B1", "a", "a"), Create("A1", "z", "z")],
            ["B1", "A1", "A5"]);

        Assert.Equal(
            ["B1 a a", "A1 z z", "A5 a b", "A5 a z", "A5 b a"],
            violations.Select(violation => $"{violation.RuleId} {violation.Source} {violation.Target}"));
    }

    [Fact]
    public void OrderingIsOrdinalSoPlatformsAgree()
    {
        var violations = Violation.Normalize([Create("A5", "a", "x"), Create("A5", "B", "x")], ["A5"]);

        Assert.Equal(["B", "a"], violations.Select(violation => violation.Source));
    }

    [Fact]
    public void ARuleTheOrderDoesNotListSortsLast()
    {
        var violations = Violation.Normalize([Create("Z9", "a", "a"), Create("A1", "a", "a")], ["A1"]);

        Assert.Equal(["A1", "Z9"], violations.Select(violation => violation.RuleId));
    }
}
