using System.Text;
using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Support;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>H2 over the break record: new entries are complete and match a break, and history never changes (FR-033, FR-036, US1 scenario 5).</summary>
public class BreakRecordTests
{
    private const string Item = "method Widget::Size()";

    private static readonly BreakEntry Valid = new("fixture", Item, "S2", "Replaced by Depth; see ISK-0099.", "Architecture (ADR) owner", 2, "2026-11-20");

    private static readonly FoundBreak Removed = new("fixture", Item, "S2");

    private static readonly BreakEntry History = new("fixture", "type Gadget", "S1", "Merged into Widget.", "Architecture (ADR) owner", 2, "2026-10-01");

    private static BreakRecordCheck Check(IReadOnlyList<BreakEntry> baseEntries, IReadOnlyList<BreakEntry> current, params FoundBreak[] breaks) =>
        BreakRecord.Check(baseEntries, current, breaks, "base");

    private static string OnlyH2(BreakRecordCheck check)
    {
        var violation = Assert.Single(check.Violations);
        Assert.Equal("H2", violation.RuleId);
        Assert.Empty(check.Declared);
        return violation.WhatChanged;
    }

    [Fact]
    public void AMatchingNewEntryDeclaresTheBreak()
    {
        var check = Check([History], [History, Valid], Removed);

        Assert.Empty(check.Violations);
        Assert.True(check.IsDeclared(Removed));
        Assert.Equal("declared: S2 fixture method Widget::Size(): Replaced by Depth; see ISK-0099.", Assert.Single(check.Declared).ToString());
    }

    [Fact]
    [ProvesRule("H2")]
    public void AnotherRoleInApprovedByFailsH2()
    {
        Assert.Equal("approvedBy is \"Spec Kit maintainer\", not \"Architecture (ADR) owner\"", OnlyH2(Check([], [Valid with { ApprovedBy = "Spec Kit maintainer" }], Removed)));
    }

    [Fact]
    [ProvesRule("H2")]
    public void AnEntryMatchingNoBreakFailsH2()
    {
        Assert.Equal("matches no break in this change", OnlyH2(Check([], [Valid])));
    }

    [Fact]
    [ProvesRule("H2")]
    public void TwoIdenticalNewEntriesFailH2()
    {
        var check = Check([], [Valid, Valid], Removed);

        var violation = Assert.Single(check.Violations);
        Assert.Equal(("H2", "repeats another new entry"), (violation.RuleId, violation.WhatChanged));
        Assert.Single(check.Declared);
    }

    [Fact]
    [ProvesRule("H2")]
    public void AnEntryVersionOtherThanTheOneTheBreakLandsInFailsH2()
    {
        var check = BreakRecord.Check([], [Valid with { Version = 7 }], [Removed], "base", entry => entry.Contract == "fixture" ? 2 : null);

        Assert.Equal("version is 7, but the break lands in fixture@2", OnlyH2(check));
        Assert.Empty(BreakRecord.Check([], [Valid], [Removed], "base", _ => 2).Violations);
    }

    [Fact]
    [ProvesRule("H2")]
    public void ARepeatedHistoricalEntryFailsH2()
    {
        Assert.Equal("repeats a historical entry", OnlyH2(Check([History], [History, History])));
    }

    [Fact]
    [ProvesRule("H2")]
    public void AnEditedHistoricalEntryFailsH2()
    {
        Assert.Equal("historical entry edited", OnlyH2(Check([History], [History with { Reason = "Rewritten." }])));
    }

    [Fact]
    [ProvesRule("H2")]
    public void ARemovedHistoricalEntryFailsH2()
    {
        Assert.Equal("historical entry removed", OnlyH2(Check([History], [])));
    }

    [Theory]
    [ProvesRule("H2")]
    [InlineData("reason")]
    [InlineData("version")]
    [InlineData("date")]
    public void AnIncompleteEntryFailsH2(string field)
    {
        var entry = field switch
        {
            "reason" => Valid with { Reason = " " },
            "version" => Valid with { Version = null },
            _ => Valid with { Date = null },
        };

        Assert.Equal($"incomplete: {field} missing", OnlyH2(Check([], [entry], Removed)));
    }

    [Fact]
    [ProvesRule("H2")]
    public void AKindThatIsNotABreakKindFailsH2()
    {
        Assert.Equal("kind V2 is not a break kind", OnlyH2(Check([], [Valid with { Kind = "V2" }], Removed)));
    }

    [Fact]
    [ProvesRule("H2")]
    public void ADateInAnotherFormatFailsH2()
    {
        Assert.Equal("date \"20/11/2026\" is not yyyy-MM-dd", OnlyH2(Check([], [Valid with { Date = "20/11/2026" }], Removed)));
    }

    [Fact]
    public void TheRecordParsesFromItsJsonShape()
    {
        const string json = """
            { "entries": [ { "contract": "fixture", "item": "method Widget::Size()", "kind": "S2", "reason": "Replaced by Depth; see ISK-0099.",
              "approvedBy": "Architecture (ADR) owner", "version": 2, "date": "2026-11-20" }, { "contract": "fixture", "version": "2" } ] }
            """;

        var entries = BreakRecord.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal(Valid, entries[0]);
        Assert.Equal(new BreakEntry("fixture", null, null, null, null, null, null), entries[1]);
        Assert.Empty(BreakRecord.Parse(null));
    }
}
