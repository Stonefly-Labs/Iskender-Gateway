using System.Text.Json.Nodes;
using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class ModuleMapRuleTests
{
    private const string Document = "docs/03-domain-and-technical-architecture.md";

    private const string PlannedRow = "| Plugins | approval,";

    private static IReadOnlyList<Violation> RunMutated(Action<JsonObject> mutate)
    {
        using var fixture = RepositoryFixture.Copy();
        Mutate(fixture, mutate);
        return RuleRunner.Run(fixture.Root);
    }

    private static void Mutate(RepositoryFixture fixture, Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(fixture.Read(RuleSource.RelativePath))!.AsObject();
        mutate(node);
        fixture.Write(RuleSource.RelativePath, node.ToJsonString());
    }

    [Fact]
    public void TheRealRepositoryResolvesTwelveOfTwelveModules()
    {
        var root = RepositoryPaths.Root();
        var source = RuleSource.Load(root).Source!;
        var table = MarkdownTable.UnderHeading(File.ReadLines(Path.Combine(root, Document)), source.ModuleTableHeading)![0];

        Assert.Equal(12, table.Rows.Count);
        Assert.Equal(12, source.Modules.Count);
        Assert.Equal(
            table.Rows.Select(row => row[0]).Order(StringComparer.Ordinal),
            source.Modules.Select(module => module.Module).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(RuleRunner.Run(root), violation => violation.RuleId is "M1" or "M2");
    }

    [Fact]
    [ProvesRule("M1")]
    public void AMissingMapEntryFailsNamingTheModule()
    {
        var violations = RunMutated(node => node["modules"]!.AsArray().RemoveAt(0));

        Assert.Contains(violations, violation => violation.RuleId == "M1" && violation.Target == "Hosting/Ingress");
    }

    [Fact]
    [ProvesRule("M1")]
    public void AnAddedDocumentRowFailsNamingTheModule()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Replace(Document, PlannedRow, $"| Scheduler | jobs | contracts | nothing |\n{PlannedRow}");

        Assert.Contains(RuleRunner.Run(fixture.Root), violation =>
            violation.RuleId == "M1" && violation.Source == Document && violation.Target == "Scheduler");
    }

    [Fact]
    [ProvesRule("M1")]
    public void ADuplicateMapEntryFailsNamingTheModule()
    {
        var violations = RunMutated(node =>
        {
            var modules = node["modules"]!.AsArray();
            modules.Add(modules[0]!.DeepClone());
        });

        Assert.Contains(violations, violation =>
            violation.RuleId == "M1" && violation.Target == "Hosting/Ingress" && violation.Detail.Contains("more than one", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("M1")]
    public void AnEntryForAModuleTheDocumentDoesNotListFailsNamingIt()
    {
        var violations = RunMutated(node =>
            node["modules"]!.AsArray().Add(new JsonObject { ["module"] = "Ghost", ["assembly"] = "Iskender.Domain", ["namespaceRoot"] = "Iskender.Domain.Ghost" }));

        Assert.Contains(violations, violation => violation.RuleId == "M1" && violation.Target == "Ghost");
    }

    [Fact]
    [ProvesRule("M1")]
    public void ModuleNamesCompareCaseSensitively()
    {
        var violations = RunMutated(node => node["modules"]![0]!["module"] = "hosting/ingress");

        Assert.Contains(violations, violation => violation.RuleId == "M1" && violation.Target == "hosting/ingress");
        Assert.Contains(violations, violation => violation.RuleId == "M1" && violation.Target == "Hosting/Ingress");
    }

    [Fact]
    [ProvesRule("M2")]
    public void AMapEntryNamingAMissingProjectFails()
    {
        var violations = RunMutated(node => node["modules"]![0]!["assembly"] = "Iskender.Absent");

        Assert.Contains(violations, violation => violation.RuleId == "M2" && violation.Source == "Hosting/Ingress" && violation.Target == "Iskender.Absent");
    }

    [Fact]
    [ProvesRule("M2")]
    public void AMapEntryNamingAnUnapprovedPatternFails()
    {
        var violations = RunMutated(node =>
        {
            var providers = node["modules"]!.AsArray().Single(module => module!["module"]!.GetValue<string>() == "Providers")!;
            providers["assembly"] = "Iskender.Providers.Abstractions, with adapters in Iskender.Absent.<Provider>";
        });

        Assert.Contains(violations, violation => violation.RuleId == "M2" && violation.Target == "Iskender.Absent.<Provider>");
    }

    [Fact]
    [ProvesRule("M2")]
    public void APortEntryNamingAMissingProjectFails()
    {
        var violations = RunMutated(node => node["portHomes"]![0]!["home"] = "Iskender.Absent");

        Assert.Contains(violations, violation => violation.RuleId == "M2" && violation.Target == "Iskender.Absent");
    }

    [Fact]
    [ProvesRule("M2")]
    public void APortEntryNamingAnUnmappedDomainNamespaceFails()
    {
        var violations = RunMutated(node => node["portHomes"]![0]!["home"] = "Iskender.Domain.Unmapped");

        Assert.Contains(violations, violation => violation.RuleId == "M2" && violation.Target == "Iskender.Domain.Unmapped");
    }

    [Fact]
    [ProvesRule("M2")]
    public void ADuplicateNamespaceRootFails()
    {
        var violations = RunMutated(node => node["modules"]![2]!["namespaceRoot"] = node["modules"]![1]!["namespaceRoot"]!.GetValue<string>());

        Assert.Contains(violations, violation => violation.RuleId == "M2" && violation.Target == "Iskender.Domain.Identity");
    }

    [Fact]
    [ProvesRule("M2")]
    public void ADomainModuleWithoutAChildNamespaceFails()
    {
        var violations = RunMutated(node => node["modules"]![1]!["namespaceRoot"] = "Iskender.Other");

        Assert.Contains(violations, violation => violation.RuleId == "M2" && violation.Source == "Identity/Authorization" && violation.Target == "Iskender.Other");
    }

    [Fact]
    public void ThePortEntryHomedInTheSharedNamespacePasses()
    {
        var source = RuleSource.Load(RepositoryPaths.Root()).Source!;

        Assert.Contains(source.PortHomes, port => port.Home == source.ModuleNamespaces.Shared);
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId == "M2");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AMissingHeadingFailsRatherThanPassing()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Replace(Document, "## Module boundaries", "## Something else");

        Assert.Contains(RuleRunner.Run(fixture.Root), violation =>
            violation.RuleId == "C2" && violation.Source == Document && violation.Target == "Module boundaries");
    }

    [Fact]
    [ProvesRule("C2")]
    public void ATableWithNoRowsFailsRatherThanPassing()
    {
        using var fixture = RepositoryFixture.Copy();
        var lines = fixture.Read(Document).Split('\n').ToList();
        var header = lines.FindIndex(line => line.StartsWith("| Module | Owns |", StringComparison.Ordinal));
        lines.RemoveRange(header + 2, lines.Skip(header + 2).TakeWhile(line => line.StartsWith('|')).Count());
        fixture.Write(Document, string.Join('\n', lines));

        Assert.Contains(RuleRunner.Run(fixture.Root), violation => violation.RuleId == "C2" && violation.Source == Document);
    }
}
