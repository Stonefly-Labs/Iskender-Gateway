using Iskender.Contracts.Compatibility.Fixtures.Reader;
using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Support;
using Iskender.Contracts.Compatibility.Tests.Surface;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>Each V rule fails on its compiled negative fixture, read as contract <c>fixture</c> (FR-027, FR-029, FR-030, FR-032).</summary>
public class VersionRuleTests
{
    private const string Contract = "fixture";

    private static (IReadOnlyList<string> Before, IReadOnlyList<string> After) Read(string rule)
    {
        var assembly = typeof(Sample).Assembly;
        return (
            SurfaceReader.Read(assembly, $"Iskender.Contracts.Compatibility.Fixtures.Kinds.{rule}.Before"),
            SurfaceReader.Read(assembly, $"Iskender.Contracts.Compatibility.Fixtures.Kinds.{rule}.After"));
    }

    private static IEnumerable<SurfaceChange> OnlyV2(IEnumerable<SurfaceChange> changes) => changes.Where(change => change.RuleId == "V2");

    private static List<string> Describe(IEnumerable<SurfaceChange> changes) =>
        changes.Select(change => $"{change.RuleId} {change.Item}: {change.WhatChanged}").Order(StringComparer.Ordinal).ToList();

    [Fact]
    [ProvesRule("V1")]
    [Trait("Category", "BreakCatalog")]
    public void AnUntaggedTypeOrNewMemberFailsV1()
    {
        var (before, after) = Read("V1");

        Assert.Equal(
            [
                "V1 property Widget::Depth: member added after its type has no tag of its own",
                "V1 property Widget::Width: version tag other@2 does not name fixture",
                "V1 type Gadget: no version tag naming fixture",
            ],
            Describe(VersionRules.Tags(Contract, before, after)));
    }

    [Fact]
    [ProvesRule("V1")]
    [Trait("Category", "BreakCatalog")]
    public void ANewItemTaggedWithAnotherVersionThanTheCurrentOneFailsV1()
    {
        var (before, after) = Read("V1Since");

        Assert.Equal(
            [
                "V1 property Widget::Width: new item tagged fixture@7, expected fixture@2, the package's current version",
                "V1 type Gadget: new item tagged fixture@1, expected fixture@2, the package's current version",
            ],
            Describe(VersionRules.Check(Contract, before, 1, after, 2)));
    }

    [Fact]
    [ProvesRule("V2")]
    [Trait("Category", "BreakCatalog")]
    public void AChangedSurfaceWithoutAVersionIncreaseFailsV2()
    {
        var (before, after) = Read("V2");

        // The new member's tag also disagrees with version 1, which V1 reports; this test is about V2.
        var change = Assert.Single(OnlyV2(VersionRules.Check(Contract, before, 1, after, 1)));
        Assert.Equal(("V2", "version is 1, expected 2: the surface changed"), (change.RuleId, change.WhatChanged));
        Assert.Empty(VersionRules.Check(Contract, before, 1, after, 2));
    }

    [Fact]
    [ProvesRule("V2")]
    [Trait("Category", "BreakCatalog")]
    public void AnUnchangedSurfaceWithAVersionIncreaseFailsV2()
    {
        var (before, _) = Read("V2");

        Assert.Equal(["V2 package fixture: version is 2, expected 1: the surface is unchanged"], Describe(VersionRules.Check(Contract, before, 1, before, 2)));
        Assert.Empty(VersionRules.Check(Contract, before, 1, before, 1));
    }

    [Fact]
    [ProvesRule("V2")]
    [Trait("Category", "BreakCatalog")]
    public void AFirstBaselineStartsAtVersionOne()
    {
        var (before, _) = Read("V2");

        // Every item is new and tagged 1, which V1 also reports at version 2; this test is about V2.
        Assert.Equal(["V2 package fixture: version is 2, expected 1: the package has no baseline at the base"], Describe(OnlyV2(VersionRules.Check(Contract, null, null, before, 2))));
        Assert.Empty(VersionRules.Check(Contract, null, null, before, 1));
    }

    [Fact]
    [ProvesRule("V3")]
    [Trait("Category", "BreakCatalog")]
    public void ChangingAPublishedTagFailsV3()
    {
        var (before, after) = Read("V3");

        Assert.Equal(["V3 type Widget: tag changed from fixture@1 to fixture@2"], Describe(VersionRules.Tags(Contract, before, after)));
    }

    [Fact]
    [ProvesRule("V4")]
    [Trait("Category", "BreakCatalog")]
    public void ADeprecationWithoutItsVersionAndReplacementFailsV4()
    {
        var (before, after) = Read("V4");

        Assert.Equal(
            [
                "V4 property Widget::Depth: deprecation message \"Deprecated in other@1. No replacement.\" does not state its version and replacement",
                "V4 property Widget::Size: deprecation message \"Use Depth instead.\" does not state its version and replacement",
            ],
            Describe(VersionRules.Tags(Contract, before, after)));
    }

    [Theory]
    [InlineData("Deprecated in fixture@1. No replacement.")]
    [InlineData("Deprecated in fixture@12. Use Widget.Depth.")]
    public void AWellFormedDeprecationPasses(string message)
    {
        var line = new SurfaceLine("method", "Widget::Size()", [new("obsolete", message), new("since", "fixture@1")]);

        Assert.Empty(VersionRules.Tags(Contract, [], ["type Widget | since=fixture@1", line.ToString()]));
    }

    [Fact]
    public void TheCompatibleFixturesAreTaggedConsistently()
    {
        var (before, after) = Read("V2");

        Assert.Empty(VersionRules.Tags(Contract, before, after));
    }
}
