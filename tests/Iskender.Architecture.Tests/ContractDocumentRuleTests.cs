using System.Text.Json.Nodes;
using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class ContractDocumentRuleTests
{
    private const string Contract = "specs/0006-host-composition-di-registration/contracts/module-dependency-rules.md";

    private const string CodeOwners = ".github/CODEOWNERS";

    private const string CiWorkflow = ".github/workflows/ci.yml";

    private const string DocsGate = ".github/scripts/docs-gate.sh";

    private const string Contributing = "CONTRIBUTING.md";

    private static IReadOnlyList<Violation> Run(Action<RepositoryFixture> mutate)
    {
        using var fixture = RepositoryFixture.Copy();
        mutate(fixture);
        return RuleRunner.Run(fixture.Root);
    }

    [Fact]
    public void TheRealRepositoryIsCovered()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId is "C3");
    }

    [Theory]
    [InlineData("/tests/Iskender.Architecture.Tests/", "tests/Iskender.Architecture.Tests/module-dependency-rules.json")]
    [InlineData("/.editorconfig", ".editorconfig")]
    [InlineData("/Directory.Build.props", "Directory.Build.props")]
    [InlineData("/Directory.Packages.props", "Directory.Packages.props")]
    [InlineData("/Directory.Build.targets", "Directory.Build.targets")]
    [InlineData("/src/Iskender.Core.Contracts/", "src/Iskender.Core.Contracts/Iskender.Core.Contracts.csproj")]
    [InlineData("/src/Iskender.Providers.Abstractions/", "src/Iskender.Providers.Abstractions/Iskender.Providers.Abstractions.csproj")]
    [InlineData("/src/Iskender.Policy.Sdk/", "src/Iskender.Policy.Sdk/Iskender.Policy.Sdk.csproj")]
    [InlineData("/src/Iskender.Domain/", "src/Iskender.Domain/Iskender.Domain.csproj")]
    [InlineData("/src/Iskender.Infrastructure/", "src/Iskender.Infrastructure/Iskender.Infrastructure.csproj")]
    [InlineData("/src/Iskender.Management/", "src/Iskender.Management/Iskender.Management.csproj")]
    [InlineData("/src/Iskender.Hosting/", "src/Iskender.Hosting/Iskender.Hosting.csproj")]
    [InlineData("/src/Iskender.Gateway.Host/", "src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj")]
    [InlineData("/src/Iskender.ControlPlane.Host/", "src/Iskender.ControlPlane.Host/Iskender.ControlPlane.Host.csproj")]
    [ProvesRule("C3")]
    public void RemovingACoverEntryFailsNamingThePath(string pattern, string path)
    {
        var violations = Run(fixture => fixture.Replace(CodeOwners, $"{pattern} @christopherhouse", string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Source == CodeOwners && violation.Target == path);
    }

    [Fact]
    [ProvesRule("C3")]
    public void ANewProjectWithNoCoverFails()
    {
        const string Project = "src/Iskender.Providers.Sample/Iskender.Providers.Sample.csproj";

        var violations = Run(fixture => fixture.Write(Project, "<Project />"));

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Target == Project);
    }

    [Fact]
    [ProvesRule("C3")]
    public void ASampleWithNoCoverFails()
    {
        const string Project = "samples/Iskender.Samples.EmbeddedHost/Iskender.Samples.EmbeddedHost.csproj";

        var violations = Run(fixture =>
        {
            // Once the hosting sample exists, its own entry is removed first, so the sample alone is uncovered.
            fixture.Write(CodeOwners, fixture.Read(CodeOwners).Replace("/samples/ @christopherhouse", string.Empty, StringComparison.Ordinal));
            fixture.Write(Project, """<Project Sdk="Microsoft.NET.Sdk.Web" />""");
        });

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Target == Project);
    }

    [Fact]
    [ProvesRule("C3")]
    public void ALaterEntryWithNoOwnerTakesCoverAway()
    {
        var violations = Run(fixture => fixture.Write(CodeOwners, $"{fixture.Read(CodeOwners)}\n/src/Iskender.Domain/\n"));

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Target == "src/Iskender.Domain/Iskender.Domain.csproj");
    }

    [Fact]
    [ProvesRule("C3")]
    public void ACommentedOutEntryDoesNotCover()
    {
        var violations = Run(fixture => fixture.Replace(CodeOwners, "/.editorconfig @christopherhouse", "# /.editorconfig @christopherhouse"));

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Target == ".editorconfig");
    }

    [Theory]
    [InlineData("* @christopherhouse")]
    [InlineData("/** @christopherhouse")]
    [InlineData("*.csproj @christopherhouse")]
    [InlineData("/src/*/*.csproj @christopherhouse")]
    public void AWiderPatternCovers(string entry)
    {
        var violations = Run(fixture => fixture.Write(CodeOwners, $"{fixture.Read(CodeOwners).Replace("/src/Iskender.Domain/ @christopherhouse", string.Empty, StringComparison.Ordinal)}\n{entry}\n"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "C3" && violation.Target == "src/Iskender.Domain/Iskender.Domain.csproj");
    }

    [Fact]
    public void AMissingOwnersFileIsAnUnreadableInput()
    {
        var violations = Run(fixture => fixture.Delete(CodeOwners));

        Assert.Contains(violations, violation => violation.RuleId == "C2" && violation.Source == CodeOwners);
    }

    [Fact]
    public void TheRealCiAndDocumentationGateCoverEveryProject()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId is "C4");
    }

    [Theory]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("C4")]
    public void AnExecutableMissingFromTheAttestationFails(string name)
    {
        var violations = Run(fixture => fixture.Replace(CiWorkflow, $"            src/{name}/bin/${{{{ env.CONFIGURATION }}}}/net10.0/{name}.dll\n", string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Source == CiWorkflow && violation.Target == name && violation.Detail.Contains("subject-path", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("C4")]
    public void AnExecutableMissingFromTheChecksumStepFails(string name)
    {
        var violations = Run(fixture => fixture.Replace(CiWorkflow, $"\"src/{name}/bin/$CONFIGURATION/net10.0/{name}.dll\"", string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Source == CiWorkflow && violation.Target == name && violation.Detail.Contains("sha256sum", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("C4")]
    public void APathOnlyInACommentDoesNotCountAsAttested()
    {
        const string Path = "src/Iskender.ControlPlane.Host/bin/${{ env.CONFIGURATION }}/net10.0/Iskender.ControlPlane.Host.dll";

        var violations = Run(fixture => fixture.Replace(CiWorkflow, Path, $"# {Path}"));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Target == "Iskender.ControlPlane.Host");
    }

    [Theory]
    [InlineData("Iskender.Core.Contracts")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [InlineData("Iskender.Hosting")]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("C4")]
    public void ABehaviorBearingPathMissingFromTheGateScriptFails(string name)
    {
        var violations = Run(fixture => fixture.Replace(DocsGate, $"src/{name}/*", "src/Removed/*"));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Source == DocsGate && violation.Target == name);
    }

    [Theory]
    [InlineData("Iskender.Core.Contracts")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [InlineData("Iskender.Hosting")]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("C4")]
    public void ABehaviorBearingPathMissingFromContributingFails(string name)
    {
        var violations = Run(fixture => fixture.Replace(Contributing, $"`src/{name}/**`", "`src/Removed/**`"));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Source == Contributing && violation.Target == name);
    }

    [Fact]
    public void TheRealContractMatchesTheRuleSource()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId == "C1");
    }

    [Fact]
    [ProvesRule("C1")]
    public void AMismatchedVersionInTheContractFails()
    {
        var violations = Run(fixture => fixture.Replace(Contract, "**Version**: 4", "**Version**: 5"));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == Contract && violation.Target == "version");
    }

    [Fact]
    [ProvesRule("C1")]
    public void AMismatchedVersionInTheRuleSourceFails()
    {
        var violations = Run(fixture => MutateSource(fixture, node => node["contract"]!["version"] = 5));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Target == "version");
    }

    [Theory]
    [InlineData("Module map", "| Hosting/Ingress | `Iskender.Hosting` | `Iskender.Hosting` |", "| Hosting/Ingress | `Iskender.Hosting` | `Iskender.Hostess` |")]
    [InlineData("Port homes", "| `IProviderAdapter` | `Iskender.Providers.Abstractions` | ISK-0030 |", "| `IProviderAdapter` | `Iskender.Providers.Abstractions` | ISK-0031 |")]
    [InlineData("Project roles", "| `Iskender.Hosting` | public contract |", "| `Iskender.Hosting` | executable |")]
    [InlineData("Project roles", "| Any project under `tests/` | test |", "| Any project under `testz/` | test |")]
    [InlineData("Approved direct references", "| `Iskender.Hosting` | `Iskender.Domain`, `Iskender.Core.Contracts` |", "| `Iskender.Hosting` | `Iskender.Domain` |")]
    [InlineData("Approved direct references", "| `Iskender.Core.Contracts` | (nothing internal) |", "| `Iskender.Core.Contracts` | `Iskender.Domain` |")]
    [InlineData("Module namespaces", "| `Iskender.Domain.Identity` | `Iskender.Domain.Ports` |", "| `Iskender.Domain.Identity` | `Iskender.Domain.Policy` |")]
    [InlineData("Required references", "| `Iskender.ControlPlane.Host` | `Iskender.Management`, `Iskender.Infrastructure` |", "| `Iskender.ControlPlane.Host` | `Iskender.Management` |")]
    [InlineData("Forbidden reach", "| A11 | `Iskender.Infrastructure` |", "| A11 | `Iskender.Domain` |")]
    [InlineData("Rules", "| M1 | Each module", "| M1 | Every module")]
    [InlineData("Denied categories", "| `data-access` | `Microsoft.EntityFrameworkCore`, `Dapper`,", "| `data-access` | `Microsoft.EntityFrameworkCore`,")]
    [InlineData("Denied categories", "| `Microsoft.AspNetCore.App` | `Microsoft.NET.Sdk.Web` |", "| `Microsoft.AspNetCore.Other` | `Microsoft.NET.Sdk.Web` |")]
    [InlineData("Denied categories", "| `Microsoft.AspNetCore.App` | `Microsoft.NET.Sdk.Web` |", "| `Microsoft.AspNetCore.App` | `Microsoft.NET.Sdk.Other` |")]
    [InlineData("Denied by project", "| `Iskender.Infrastructure.<Adapter>` | `provider` |", "| `Iskender.Infrastructure.<Adapter>` | `driver` |")]
    [InlineData("Approved SDKs", "| `Microsoft.NET.Sdk.Web` | Executables and samples |", "| `Microsoft.NET.Sdk.Web` | Everything |")]
    [InlineData("Required settings", "| `AnalysisLevel` | `10.0-recommended` |", "| `AnalysisLevel` | `10.0-minimum` |")]
    [InlineData("Listed suppressions", "| `NU1900` |", "| `NU1901` |")]
    [ProvesRule("C1")]
    public void AChangedContractRowFailsInBothDirections(string section, string row, string changed)
    {
        var violations = Run(fixture => fixture.Replace(Contract, row, changed));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == Contract && violation.Target.StartsWith($"{section}: ", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == RuleSource.RelativePath && violation.Target.StartsWith($"{section}: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Forbidden settings", "`RunAnalyzers`, ", "RunAnalyzers")]
    [InlineData("Forbidden items", "`EditorConfigFiles`, ", "EditorConfigFiles")]
    [ProvesRule("C1")]
    public void AnEntryRemovedFromTheContractProseFails(string section, string text, string entry)
    {
        var violations = Run(fixture => fixture.Replace(Contract, text, string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == RuleSource.RelativePath && violation.Target == $"{section}: {entry}");
    }

    [Theory]
    [InlineData("Forbidden settings", "`RunAnalyzers`, ", "`RunAnalyzers`, `Invented`, ", "Invented")]
    [InlineData("Forbidden items", "`EditorConfigFiles`, ", "`EditorConfigFiles`, `Invented`, ", "Invented")]
    [ProvesRule("C1")]
    public void AnEntryAddedToTheContractProseFails(string section, string text, string changed, string entry)
    {
        var violations = Run(fixture => fixture.Replace(Contract, text, changed));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == Contract && violation.Target == $"{section}: {entry}");
    }

    public static TheoryData<string> EverySourceMutation => [.. SourceMutations.Keys];

    private static readonly Dictionary<string, Action<JsonObject>> SourceMutations = new()
    {
        ["Module map"] = node => node["modules"]![0]!["namespaceRoot"] = "Iskender.Hostess",
        ["Port homes"] = node => node["portHomes"]![0]!["owner"] = "ISK-9999",
        ["Project roles"] = node => Project(node, "Iskender.Hosting")["role"] = "executable",
        ["Approved direct references"] = node => Project(node, "Iskender.Hosting")["references"]!.AsArray().RemoveAt(0),
        ["Module namespaces"] = node => node["moduleNamespaces"]!["allowList"]!.AsArray().Add(new JsonObject { ["from"] = "Iskender.Domain.Identity", ["to"] = "Iskender.Domain.Policy" }),
        ["Required references"] = node => node["requiredReferences"]!["Iskender.Gateway.Host"]!.AsArray().RemoveAt(0),
        ["Forbidden reach"] = node => node["forbiddenReach"]![0]!["to"]!.AsArray().RemoveAt(0),
        ["Rules"] = node => node["rules"]![0]!["statement"] = "Something else.",
        ["Denied categories"] = node => node["deniedPackages"]!["cloud"]!["prefixes"]!.AsArray().Add("Invented"),
        ["Denied by project"] = node => Project(node, "Iskender.Infrastructure")["denied"]!.AsArray().Add("data-access"),
        ["Approved SDKs"] = node => node["approvedSdks"]![0]!["usedBy"] = "Something else",
        ["Required settings"] = node => node["protectedSettings"]!["required"]![0]!["value"] = "net11.0",
        ["Listed suppressions"] = node => node["suppressions"]![0]!["reason"] = "Something else.",
    };

    private static JsonObject Project(JsonObject node, string name) =>
        node["projects"]!.AsArray().First(project => (string?)project!["name"] == name)!.AsObject();

    private static void MutateSource(RepositoryFixture fixture, Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(fixture.Read(RuleSource.RelativePath))!.AsObject();
        mutate(node);
        fixture.Write(RuleSource.RelativePath, node.ToJsonString());
    }

    [Theory]
    [MemberData(nameof(EverySourceMutation))]
    [ProvesRule("C1")]
    public void AChangedSourceRowFailsInBothDirections(string section)
    {
        var violations = Run(fixture => MutateSource(fixture, SourceMutations[section]));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == Contract && violation.Target.StartsWith($"{section}: ", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == RuleSource.RelativePath && violation.Target.StartsWith($"{section}: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("forbidden", "Invented", "Forbidden settings")]
    [InlineData("forbiddenItems", "Invented", "Forbidden items")]
    [ProvesRule("C1")]
    public void AnEntryAddedToTheSourceListFails(string list, string entry, string section)
    {
        var violations = Run(fixture => MutateSource(fixture, node => node["protectedSettings"]![list]!.AsArray().Add(entry)));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == RuleSource.RelativePath && violation.Target == $"{section}: {entry}");
    }

    [Fact]
    [ProvesRule("C1")]
    public void ARetiredRuleMustBeMarkedInTheContract()
    {
        var violations = Run(fixture => MutateSource(fixture, node => node["rules"]![0]!["retired"] = true));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == RuleSource.RelativePath && violation.Target.StartsWith("Rules: A1 | (retired)", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("C1")]
    public void ARemovedSectionFails()
    {
        var violations = Run(fixture => fixture.Replace(Contract, "## Approved SDKs", "## Renamed SDKs"));

        Assert.Contains(violations, violation => violation.RuleId == "C1" && violation.Source == RuleSource.RelativePath && violation.Target.StartsWith("Approved SDKs: ", StringComparison.Ordinal));
    }

    [Fact]
    public void ReorderingAListInAContractRowPasses()
    {
        var violations = Run(fixture => fixture.Replace(
            Contract,
            "| `Iskender.Hosting` | `Iskender.Domain`, `Iskender.Core.Contracts` |",
            "| `Iskender.Hosting` | `Iskender.Core.Contracts`, `Iskender.Domain` |"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "C1");
    }

    [Theory]
    [InlineData(CiWorkflow)]
    [InlineData(DocsGate)]
    [InlineData(Contributing)]
    public void AMissingInputIsUnreadable(string path)
    {
        var violations = Run(fixture => fixture.Delete(path));

        Assert.Contains(violations, violation => violation.RuleId == "C2" && violation.Source == path);
    }
}
