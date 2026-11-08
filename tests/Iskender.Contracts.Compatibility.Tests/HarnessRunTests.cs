using System.Text.Json.Nodes;
using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Registries;
using Iskender.Contracts.Compatibility.Tests.Support;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>
/// The whole run over the checkout, with one input changed at a time (FR-034, FR-036, FR-037, FR-041). The base and the
/// checkout start equal, each with freshly generated baselines and the real break record, so any violation comes from the
/// change under test, and a break the repository has already declared stays history on both sides.
/// </summary>
public class HarnessRunTests
{
    private const string WireV1 = HarnessRun.WireDirectory + "/error-envelope/v1/";

    private static readonly DirectoryFileSet Checkout = new(Repository.Root(), "the checkout");

    private static readonly IReadOnlyList<ContractPackageInput> RealPackages =
        [.. new[] { "Iskender.Core.Contracts", "Iskender.Policy.Sdk", "Iskender.Providers.Abstractions" }.Select(project => HarnessRun.LoadReferencedPackage(project)!)];

    private const string BreakRecordHeader = "| Contract | Item | Kind | Reason | Approved by | Version | Date |";

    private static Dictionary<string, string?> Baselines()
    {
        var files = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [HarnessRun.RegistryBaselinePath] = RegistrySnapshot.Current().ToJson(),
            [RuleCatalog.BreakRecordPath] = Checkout.ReadText(RuleCatalog.BreakRecordPath),
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

    /// <summary>The real break record with one new entry for <paramref name="item"/>, landing in <paramref name="version"/>.</summary>
    private static string RecordDeclaring(string item, int version, string contract = "core-contracts", string kind = "S2")
    {
        var record = JsonNode.Parse(Checkout.ReadText(RuleCatalog.BreakRecordPath)!)!;
        record["entries"]!.AsArray().Add(new JsonObject
        {
            ["contract"] = contract,
            ["item"] = item,
            ["kind"] = kind,
            ["reason"] = "Test.",
            ["approvedBy"] = BreakRecord.Approver,
            ["version"] = version,
            ["date"] = "2026-11-20",
        });
        return record.ToJsonString();
    }

    /// <summary>
    /// The real contract with core-contracts at <paramref name="version"/> and, when <paramref name="item"/> is given, a row
    /// for its new entry after any existing rows, as H4 needs from the change that bumps the version or declares the break.
    /// </summary>
    private static string ContractDeclaring(string? item, int version)
    {
        var lines = Checkout.ReadText(RuleCatalog.ContractPath)!.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var package = lines.FindIndex(line => line.StartsWith("   | `Iskender.Core.Contracts` | `core-contracts` |", StringComparison.Ordinal));
        lines[package] = $"   | `Iskender.Core.Contracts` | `core-contracts` | {version} |";

        if (item is null)
        {
            return string.Join('\n', lines);
        }

        var end = lines.IndexOf(BreakRecordHeader) + 2;
        while (end < lines.Count && lines[end].StartsWith('|'))
        {
            end++;
        }

        lines.Insert(end, $"| `core-contracts` | `{item}` | `S2` | Test. | {BreakRecord.Approver} | {version} | 2026-11-20 |");
        return string.Join('\n', lines);
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

        var violation = OnlyRule(result, "H3");
        Assert.Equal(RegistrySnapshot.Current().ToJson(), violation.Appendix);
        Assert.StartsWith($"H3: core-contracts@{RealPackages[0].Version} baselines/registries.json: ", violation.Message, StringComparison.Ordinal);
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
        Assert.Equal(RealPackages[0].Version, first.Violations.Single(violation => violation.RuleId == "G1").Version);
        Assert.DoesNotContain("@?", first.Report(), StringComparison.Ordinal);
        Assert.Equal(first.Report(), second.Report());
    }

    [Fact]
    public void EditingTheCommittedBaselineDoesNotHideAnUndeclaredBreak()
    {
        var removed = RealPackages[0].Surface.First(line => line.StartsWith("method ", StringComparison.Ordinal));
        var load = (string project) => HarnessRun.LoadReferencedPackage(project) is { Contract: "core-contracts" } package
            ? package with { Version = package.Version + 1, Surface = [.. package.Surface.Where(line => line != removed)] }
            : HarnessRun.LoadReferencedPackage(project);

        // The committed baseline is edited to match the new surface, so H3 is quiet, but the base still has the member.
        var result = Run(
            currentChanges: new()
            {
                [$"{HarnessRun.BaselineDirectory}/core-contracts.surface.txt"] = HarnessRun.Baseline(load("Iskender.Core.Contracts")!.Surface),
                [RuleCatalog.ContractPath] = ContractDeclaring(null, RealPackages[0].Version + 1),
            },
            load: load);

        var violation = OnlyRule(result, "S2");
        Assert.StartsWith(violation.Item + " | ", removed, StringComparison.Ordinal);
    }

    [Fact]
    public void ABaseBaselineWithNoCurrentPackageReportsS1ForEachType()
    {
        var legacy = "type Legacy.Gadget | access=public; kind=class; since=legacy-sdk@1\n"
            + "method Legacy.Gadget::Run() | access=public; returns=System.Void; since=legacy-sdk@1\n"
            + "type Legacy.Widget | access=public; kind=class; since=legacy-sdk@1\n";

        var result = Run(baseChanges: new()
        {
            [$"{HarnessRun.BaselineDirectory}/legacy-sdk.surface.txt"] = legacy,
            ["src/Iskender.Legacy.Sdk/Properties/ContractPackage.cs"] = "[assembly: ContractPackage(\"legacy-sdk\", 3)]\n",
        });

        // The package is gone, so each violation carries the version it had at the base (FR-040).
        Assert.Equal(
            ["S1 legacy-sdk@3 type Legacy.Gadget", "S1 legacy-sdk@3 type Legacy.Widget"],
            result.Violations.Select(violation => $"{violation.RuleId} {violation.Contract}@{violation.Version} {violation.Item}"));
    }

    [Fact]
    public void ADeclaredBreakPassesAndIsListed()
    {
        var removed = RealPackages[0].Surface.First(line => line.StartsWith("method ", StringComparison.Ordinal));
        var item = removed[..removed.IndexOf(" | ", StringComparison.Ordinal)];
        var version = RealPackages[0].Version + 1;
        var load = (string project) => HarnessRun.LoadReferencedPackage(project) is { Contract: "core-contracts" } package
            ? package with { Version = version, Surface = [.. package.Surface.Where(line => line != removed)] }
            : HarnessRun.LoadReferencedPackage(project);
        var surface = load("Iskender.Core.Contracts")!.Surface;

        // H4 keeps the contract's break record and contract versions tables in step, so the declaring change updates them too.
        var undocumented = Run(
            currentChanges: new()
            {
                [RuleCatalog.BreakRecordPath] = RecordDeclaring(item, version),
                [$"{HarnessRun.BaselineDirectory}/core-contracts.surface.txt"] = HarnessRun.Baseline(surface),
            },
            load: load);
        var result = Run(
            currentChanges: new()
            {
                [RuleCatalog.BreakRecordPath] = RecordDeclaring(item, version),
                [$"{HarnessRun.BaselineDirectory}/core-contracts.surface.txt"] = HarnessRun.Baseline(surface),
                [RuleCatalog.ContractPath] = ContractDeclaring(item, version),
            },
            load: load);

        Assert.Equal(["H4", "H4"], undocumented.Violations.Select(violation => violation.RuleId));
        Assert.True(result.Passed, result.Report());
        Assert.Equal($"declared: S2 core-contracts {item}: Test.", Assert.Single(result.Notices).ToString());
        Assert.Contains($"declared: S2 core-contracts {item}: Test.", result.Report(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredBreakAndADeprecationPassAndAreBothListed()
    {
        var members = RealPackages[0].Surface.Where(line => line.StartsWith("method ", StringComparison.Ordinal) && !line.Contains("obsolete=", StringComparison.Ordinal)).ToList();
        var removed = members[0];
        var item = removed[..removed.IndexOf(" | ", StringComparison.Ordinal)];
        var version = RealPackages[0].Version + 1;
        var message = $"Deprecated in core-contracts@{version}. No replacement.";
        var deprecated = members[1].Replace("since=", $"obsolete={message}; since=", StringComparison.Ordinal);
        var load = (string project) => HarnessRun.LoadReferencedPackage(project) is { Contract: "core-contracts" } package
            ? package with { Version = version, Surface = [.. package.Surface.Where(line => line != removed).Select(line => line == members[1] ? deprecated : line)] }
            : HarnessRun.LoadReferencedPackage(project);

        var result = Run(
            currentChanges: new()
            {
                [RuleCatalog.BreakRecordPath] = RecordDeclaring(item, version),
                [$"{HarnessRun.BaselineDirectory}/core-contracts.surface.txt"] = HarnessRun.Baseline(load("Iskender.Core.Contracts")!.Surface),
                [RuleCatalog.ContractPath] = ContractDeclaring(item, version),
            },
            load: load);

        Assert.True(result.Passed, result.Report());
        Assert.Equal(
            [$"declared: S2 core-contracts {item}: Test.", $"deprecated: V4 core-contracts {members[1][..members[1].IndexOf(" | ", StringComparison.Ordinal)]}: {message}"],
            result.Notices.Select(notice => notice.ToString()));
    }

    [Fact]
    public void AnAddedMemberAndAnAddedOptionalWireFieldPassAndAreReportedAsCompatible()
    {
        var version = RealPackages[0].Version + 1;
        var added = $"type Iskender.Core.Contracts.Added | access=public; kind=class; sealed; since=core-contracts@{version}";
        var load = (string project) => HarnessRun.LoadReferencedPackage(project) is { Contract: "core-contracts" } package
            ? package with { Version = version, Surface = [.. package.Surface.Append(added).Order(StringComparer.Ordinal)] }
            : HarnessRun.LoadReferencedPackage(project);
        var schema = Checkout.ReadText(WireV1 + "schema.json")!.Replace(
            "{ \"name\": \"redaction_status\", \"type\": \"string\", \"required\": true }",
            "{ \"name\": \"redaction_status\", \"type\": \"string\", \"required\": true },\n    { \"name\": \"hint\", \"type\": \"string\", \"required\": false }",
            StringComparison.Ordinal);

        var result = Run(
            currentChanges: new()
            {
                [$"{HarnessRun.BaselineDirectory}/core-contracts.surface.txt"] = HarnessRun.Baseline(load("Iskender.Core.Contracts")!.Surface),
                [RuleCatalog.ContractPath] = ContractDeclaring(null, version),
                [WireV1 + "schema.json"] = schema,
            },
            load: load);

        Assert.True(result.Passed, result.Report());
        Assert.Equal(
            "Public contract harness passed.\n"
            + "compatible: core-contracts type Iskender.Core.Contracts.Added: added\n"
            + "compatible: error-envelope error-envelope/v1 field hint: optional string field added\n",
            result.Report());
    }

    [Fact]
    [ProvesRule("V2")]
    public void ADeclaredRegistryBreakWithoutAnIncrementFailsV2()
    {
        var snapshot = RegistrySnapshot.Current();
        var changed = snapshot with { ErrorCodes = [.. snapshot.ErrorCodes.Select((code, index) => index == 0 ? code with { Meaning = code.Meaning + " Changed." } : code)] };
        var item = $"error code {snapshot.ErrorCodes[0].Id}";
        var version = RealPackages[0].Version + 1;
        HarnessResult At(int at) => Run(
            currentChanges: new()
            {
                [HarnessRun.RegistryBaselinePath] = changed.ToJson(),
                [RuleCatalog.BreakRecordPath] = RecordDeclaring(item, at, kind: "G3"),
            },
            load: project => HarnessRun.LoadReferencedPackage(project) is { Contract: "core-contracts" } package ? package with { Version = at } : HarnessRun.LoadReferencedPackage(project),
            registries: () => changed);

        var unchanged = At(RealPackages[0].Version);
        var increased = At(version);

        // The change also edits a meaning the contract's Error codes table shows, so H4 reports that row either way.
        var v2 = Assert.Single(unchanged.Violations, violation => violation.RuleId == "V2");
        Assert.Equal(
            ("package core-contracts", $"version is {RealPackages[0].Version}, expected {version}: a registry or wire contract it publishes has a break"),
            (v2.Item, v2.WhatChanged));
        Assert.DoesNotContain(unchanged.Violations, violation => violation.RuleId == "G3");
        Assert.DoesNotContain(increased.Violations, violation => violation.RuleId is "V2" or "G3");
    }

    [Fact]
    [ProvesRule("V1")]
    [Trait("Category", "BreakCatalog")]
    public void RemovingATypesTagFailsV1NamingTheType()
    {
        var assembly = typeof(Fixtures.Reader.Sample).Assembly;
        var before = Surface.SurfaceReader.Read(assembly, "Iskender.Contracts.Compatibility.Fixtures.Kinds.V1Removed.Before");
        var after = Surface.SurfaceReader.Read(assembly, "Iskender.Contracts.Compatibility.Fixtures.Kinds.V1Removed.After");

        var changes = VersionRules.Tags("fixture", before, after);

        Assert.Contains(changes, change => change.RuleId == "V1" && change.Item == "type Widget" && change.WhatChanged == "no version tag naming fixture");
        Assert.Contains(changes, change => change.RuleId == "V3" && change.Item == "type Widget");
    }

    [Fact]
    public void AnIncreasedWireVersionKeepsEarlierFixturesReadableAndNeedsItsOwnFixture()
    {
        var two = new Wire.WireVersions("error-envelope", 2, [1, 2]);

        Assert.Empty(WireRuleTests.RunPair("compatible/wire/new-version", two, new WireRuleTests.VersionTwoCodec()));
        Assert.Equal(["H1 error-envelope/v2"], WireRuleTests.RunPair("compatible/wire/added-optional-field", two, new WireRuleTests.VersionTwoCodec()).Select(violation => $"{violation.RuleId} {violation.Item}"));
        Assert.Contains(
            WireRuleTests.RunPair("compatible/wire/new-version", new Wire.WireVersions("error-envelope", 2, [2]), new WireRuleTests.VersionTwoCodec()),
            violation => violation.RuleId == "W6" && violation.WhatChanged == "supported version dropped");
    }
}
