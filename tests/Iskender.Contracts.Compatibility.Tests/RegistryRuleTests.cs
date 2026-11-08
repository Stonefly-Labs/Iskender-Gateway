using Iskender.Contracts.Compatibility.Tests.Registries;
using Iskender.Contracts.Compatibility.Tests.Support;
using Iskender.Contracts.Compatibility.Tests.Surface;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>Each G rule fails on its before-and-after registry set, and the compatible set passes (FR-008, FR-021, FR-031, FR-038).</summary>
public class RegistryRuleTests
{
    private static readonly string Fixtures = Path.Combine(Repository.Root(), Repository.HarnessDirectory, "fixtures");

    internal static IReadOnlyList<SurfaceChange> RunPair(string pair) =>
        RegistryClassifier.Compare(
            RegistrySnapshot.Parse(File.ReadAllBytes(Path.Combine(Fixtures, pair, "before.json"))),
            RegistrySnapshot.Parse(File.ReadAllBytes(Path.Combine(Fixtures, pair, "after.json"))));

    private static SurfaceChange Only(string rule)
    {
        var change = Assert.Single(RunPair($"breaks/{rule}"));
        Assert.Equal(rule, change.RuleId);
        return change;
    }

    [Fact]
    [ProvesRule("G1")]
    [Trait("Category", "BreakCatalog")]
    public void RemovingAnErrorCodeFailsG1()
    {
        Assert.Equal("error code core.rate.exceeded", Only("G1").Item);
    }

    [Fact]
    [ProvesRule("G2")]
    [Trait("Category", "BreakCatalog")]
    public void MovingACodeToAnotherCategoryFailsG2()
    {
        Assert.Equal("category changed from rate_limited to invalid_request", Only("G2").WhatChanged);
    }

    [Fact]
    [ProvesRule("G3")]
    [Trait("Category", "BreakCatalog")]
    public void ChangingACodesMeaningFailsG3()
    {
        Assert.Equal("error code core.value.invalid", Only("G3").Item);
    }

    [Fact]
    [ProvesRule("G3")]
    [Trait("Category", "BreakCatalog")]
    public void ReactivatingARetiredCodeFailsG3()
    {
        var before = RegistrySnapshot.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "breaks/G3/before.json")));
        var after = before with
        {
            ErrorCodes = [.. before.ErrorCodes.Select(code => code.Status == "retired" ? code with { Status = "active" } : code)],
        };

        var change = Assert.Single(RegistryClassifier.Compare(before, after));
        Assert.Equal(("G3", "error code core.value.legacy"), (change.RuleId, change.Item));
    }

    [Fact]
    [ProvesRule("G4")]
    [Trait("Category", "BreakCatalog")]
    public void ChangingAResourceTypePrefixFailsG4()
    {
        Assert.Equal("prefix changed from org to orz", Only("G4").WhatChanged);
    }

    [Fact]
    [ProvesRule("G4")]
    [Trait("Category", "BreakCatalog")]
    public void RemovingOrReactivatingAResourceTypeFailsG4()
    {
        var before = RegistrySnapshot.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "breaks/G4/before.json")));
        var after = before with
        {
            ResourceTypes = [.. before.ResourceTypes.Where(type => type.Name != "organization").Select(type => type with { Status = "active" })],
        };

        Assert.Equal(
            ["resource type legacy: retired type became active again", "resource type organization: resource type removed"],
            RegistryClassifier.Compare(before, after).Select(change => $"{change.Item}: {change.WhatChanged}").Order(StringComparer.Ordinal));
    }

    [Fact]
    [ProvesRule("G5")]
    [Trait("Category", "BreakCatalog")]
    public void ChangingACategorysRetryRuleFailsG5()
    {
        Assert.Equal("retry rule changed from WhenResetKnown to Never", Only("G5").WhatChanged);
    }

    [Fact]
    [ProvesRule("G5")]
    [Trait("Category", "BreakCatalog")]
    public void AddingOrRemovingACategoryFailsG5()
    {
        var before = RegistrySnapshot.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "breaks/G5/before.json")));
        var after = before with
        {
            ErrorCategories = [.. before.ErrorCategories.Where(category => category.Id != "rate_limited"), new CategoryEntry("timeout", "Never")],
        };

        Assert.Equal(
            ["error category rate_limited: category removed", "error category timeout: category added"],
            RegistryClassifier.Compare(before, after).Select(change => $"{change.Item}: {change.WhatChanged}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnAddedCodeAnAddedTypeANewlyRetiredCodeAndASafeMessageChangePass()
    {
        Assert.Empty(RunPair("compatible/registries"));
    }

    [Fact]
    public void EachCompatibleRegistryChangeIsReported()
    {
        var before = RegistrySnapshot.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "compatible/registries", "before.json")));
        var after = RegistrySnapshot.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "compatible/registries", "after.json")));

        Assert.Equal(
            ["error code core.rate.exceeded: code retired", "error code core.value.invalid: safe message changed", "error code core.value.missing: code added", "resource type project: resource type added"],
            RegistryClassifier.Compatible(before, after).Select(change => $"{change.Item}: {change.WhatChanged}").Order(StringComparer.Ordinal));
        Assert.Empty(RegistryClassifier.Compatible(after, after));
    }

    [Fact]
    public void TheCurrentSnapshotIsSortedAndRoundTrips()
    {
        var current = RegistrySnapshot.Current();
        var json = current.ToJson();

        Assert.Equal(13, current.ErrorCategories.Count);
        // A later version may add codes and resource types without revising the contract, so only version 1's are counted.
        Assert.Equal(22, current.ErrorCodes.Count(code => code.Since == 1));
        Assert.Equal(13, current.ResourceTypes.Count(type => type.Since == 1));
        Assert.Equal(["error-envelope", "resource-id"], current.WireContracts.Select(contract => contract.Id));
        Assert.Equal(current.ErrorCodes.Select(code => code.Id).Order(StringComparer.Ordinal), current.ErrorCodes.Select(code => code.Id));
        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.Equal(json, RegistrySnapshot.Parse(System.Text.Encoding.UTF8.GetBytes(json)).ToJson());
        Assert.Empty(RegistryClassifier.Compare(current, current));
    }
}
