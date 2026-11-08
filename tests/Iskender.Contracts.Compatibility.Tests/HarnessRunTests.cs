using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Registries;
using Iskender.Contracts.Compatibility.Tests.Support;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>
/// The whole run over the checkout, with one input changed at a time (FR-034, FR-036, FR-037, FR-041). The base and the
/// checkout start equal, each with freshly generated baselines, so any violation comes from the change under test.
/// </summary>
public class HarnessRunTests
{
    private const string WireV1 = HarnessRun.WireDirectory + "/error-envelope/v1/";

    private static readonly DirectoryFileSet Checkout = new(Repository.Root(), "the checkout");

    private static readonly IReadOnlyList<ContractPackageInput> RealPackages =
        [.. new[] { "Iskender.Core.Contracts", "Iskender.Policy.Sdk", "Iskender.Providers.Abstractions" }.Select(project => HarnessRun.LoadReferencedPackage(project)!)];

    private static Dictionary<string, string?> Baselines()
    {
        var files = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [HarnessRun.RegistryBaselinePath] = RegistrySnapshot.Current().ToJson(),
            [RuleCatalog.BreakRecordPath] = "{\n  \"entries\": []\n}\n",
        };
        foreach (var package in RealPackages)
        {
            files[$"{HarnessRun.BaselineDirectory}/{package.Contract}.surface.txt"] = HarnessRun.Baseline(package.Surface);
        }

        return files;
    }

    private static HarnessResult Run(
        Dictionary<string, string?>? baseChanges = null,
        Dictionary<string, string?>? currentChanges = null,
        Func<string, ContractPackageInput?>? load = null,
        Func<RegistrySnapshot>? registries = null)
    {
        static OverlayFileSet Overlay(string name, Dictionary<string, string?>? changes)
        {
            var files = Baselines();
            foreach (var (path, text) in changes ?? [])
            {
                files[path] = text;
            }

            return new OverlayFileSet(Checkout, name, files);
        }

        return new HarnessRun(Overlay("base", baseChanges), Overlay("the checkout", currentChanges), load ?? HarnessRun.LoadReferencedPackage, registries ?? RegistrySnapshot.Current).Run();
    }

    private static Violation OnlyRule(HarnessResult result, string rule)
    {
        var violation = Assert.Single(result.Violations);
        Assert.Equal(rule, violation.RuleId);
        return violation;
    }

    [Fact]
    public void AnUnchangedRepositoryPasses()
    {
        var result = Run();

        Assert.True(result.Passed, result.Report());
        Assert.Equal("Public contract harness passed.\n", result.Report());
    }

    [Fact]
    [ProvesRule("H1")]
    public void NoBaseFailsH1()
    {
        var gitBase = GitBase.Resolve(Repository.Root(), out var failure, "refs/heads/no-such-branch-for-h1");

        var result = new HarnessRun(gitBase, Checkout, HarnessRun.LoadReferencedPackage, RegistrySnapshot.Current, failure).Run();

        Assert.Contains("cannot be resolved; run git fetch origin main", OnlyRule(result, "H1").WhatChanged, StringComparison.Ordinal);
    }

    [Fact]
    [ProvesRule("H1")]
    public void AMissingRuleSourceFailsH1()
    {
        var result = Run(currentChanges: new() { [HarnessRun.RuleSourcePath] = null });

        Assert.Equal($"{HarnessRun.RuleSourcePath} is missing", OnlyRule(result, "H1").WhatChanged);
    }

    [Fact]
    [ProvesRule("H1")]
    public void NoPublicContractPackageFailsH1()
    {
        var source = Checkout.ReadText(HarnessRun.RuleSourcePath)!.Replace("\"role\": \"public contract\"", "\"role\": \"embeddable\"", StringComparison.Ordinal);

        var result = Run(currentChanges: new() { [HarnessRun.RuleSourcePath] = source });

        // The base baselines now belong to no package, so their types are also reported as removed.
        Assert.Contains(result.Violations, violation => violation.RuleId == "H1" && violation.WhatChanged == "no project has role public contract");
    }

    [Fact]
    [ProvesRule("H1")]
    public void APackageWithNoBaselineFailsH1()
    {
        var result = Run(currentChanges: new() { [$"{HarnessRun.BaselineDirectory}/policy-sdk.surface.txt"] = null });

        var violation = OnlyRule(result, "H1");
        Assert.Equal(("policy-sdk", "the package has no committed baseline"), (violation.Contract, violation.WhatChanged));
    }

    [Fact]
    [ProvesRule("H1")]
    public void ASupportedWireVersionWithNoFixtureFailsH1()
    {
        var result = Run(baseChanges: new() { [WireV1] = null }, currentChanges: new() { [WireV1] = null });

        var violation = OnlyRule(result, "H1");
        Assert.Equal(("error-envelope", "error-envelope/v1"), (violation.Contract, violation.Item));
    }

    [Fact]
    [ProvesRule("H1")]
    public void APackageThatCannotBeLoadedFailsH1()
    {
        var result = Run(load: project => project == "Iskender.Policy.Sdk" ? null : HarnessRun.LoadReferencedPackage(project));

        Assert.Contains(result.Violations, violation => violation.RuleId == "H1" && violation.Item == "package Iskender.Policy.Sdk");
    }

    [Fact]
    [ProvesRule("H3")]
    public void AStaleBaselinePrintsTheRegeneratedTextUnderH3()
    {
        var path = $"{HarnessRun.BaselineDirectory}/core-contracts.surface.txt";
        var stale = HarnessRun.Baseline(RealPackages[0].Surface.Skip(1));

        var result = Run(currentChanges: new() { [path] = stale });

        var violation = OnlyRule(result, "H3");
        Assert.Equal(HarnessRun.Baseline(RealPackages[0].Surface), violation.Appendix);
        Assert.EndsWith(violation.Appendix!, violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    [ProvesRule("H3")]
    public void AStaleRegistryBaselineFailsH3()
    {
        var result = Run(currentChanges: new() { [HarnessRun.RegistryBaselinePath] = "{}\n" });

        Assert.Equal(RegistrySnapshot.Current().ToJson(), OnlyRule(result, "H3").Appendix);
    }

    [Fact]
    public void ThreeBreaksInOneChangeAreAllReportedInAStableOrder()
    {
        var removed = RealPackages[0].Surface.First(line => line.StartsWith("method ", StringComparison.Ordinal));
        var load = (string project) => HarnessRun.LoadReferencedPackage(project) is { } package && package.Contract == "core-contracts"
            ? package with { Surface = [.. package.Surface.Where(line => line != removed)] }
            : HarnessRun.LoadReferencedPackage(project);
        var snapshot = RegistrySnapshot.Current();
        var registries = () => snapshot with { ErrorCodes = [.. snapshot.ErrorCodes.Skip(1)] };
        var changes = new Dictionary<string, string?> { [WireV1 + "retry.read.json"] = null };

        var first = Run(currentChanges: changes, load: load, registries: registries);
        var second = Run(currentChanges: changes, load: load, registries: registries);

        var breaks = first.Violations.Where(violation => RuleCatalog.IsBreakKind(violation.RuleId)).Select(violation => violation.RuleId).Distinct();
        Assert.Equal(["S2", "W4", "W6", "G1"], breaks);
        Assert.Contains(first.Violations, violation => violation.RuleId == "S2" && removed.StartsWith(violation.Item, StringComparison.Ordinal));
        Assert.Contains(first.Violations, violation => violation.RuleId == "G1" && violation.Item == $"error code {snapshot.ErrorCodes[0].Id}");
        Assert.Contains(first.Violations, violation => violation.RuleId == "V2");
        Assert.Equal(first.Report(), second.Report());
    }

    [Fact]
    public void ABaseBaselineWithNoCurrentPackageReportsS1ForEachType()
    {
        var legacy = "type Legacy.Gadget | access=public; kind=class; since=legacy-sdk@1\n"
            + "method Legacy.Gadget::Run() | access=public; returns=System.Void; since=legacy-sdk@1\n"
            + "type Legacy.Widget | access=public; kind=class; since=legacy-sdk@1\n";

        var result = Run(baseChanges: new() { [$"{HarnessRun.BaselineDirectory}/legacy-sdk.surface.txt"] = legacy });

        Assert.Equal(
            ["S1 legacy-sdk type Legacy.Gadget", "S1 legacy-sdk type Legacy.Widget"],
            result.Violations.Select(violation => $"{violation.RuleId} {violation.Contract} {violation.Item}"));
    }

    [Fact]
    public void ADeclaredBreakPassesAndIsListed()
    {
        var removed = RealPackages[0].Surface.First(line => line.StartsWith("method ", StringComparison.Ordinal));
        var item = removed[..removed.IndexOf(" | ", StringComparison.Ordinal)];
        var load = (string project) => HarnessRun.LoadReferencedPackage(project) is { Contract: "core-contracts" } package
            ? package with { Version = 2, Surface = [.. package.Surface.Where(line => line != removed)] }
            : HarnessRun.LoadReferencedPackage(project);
        var surface = load("Iskender.Core.Contracts")!.Surface;
        var record = $$"""
            { "entries": [ { "contract": "core-contracts", "item": "{{item}}", "kind": "S2", "reason": "Test.",
              "approvedBy": "Architecture (ADR) owner", "version": 2, "date": "2026-11-20" } ] }
            """;

        var result = Run(
            currentChanges: new()
            {
                [RuleCatalog.BreakRecordPath] = record,
                [$"{HarnessRun.BaselineDirectory}/core-contracts.surface.txt"] = HarnessRun.Baseline(surface),
            },
            load: load);

        Assert.True(result.Passed, result.Report());
        Assert.Equal($"declared: S2 core-contracts {item}: Test.", Assert.Single(result.Notices).ToString());
        Assert.Contains($"declared: S2 core-contracts {item}: Test.", result.Report(), StringComparison.Ordinal);
    }
}
