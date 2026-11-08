using System.Text.Json.Nodes;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class RuleSourceTests
{
    private const string SourceFile = RuleSource.RelativePath;

    private static RuleSourceLoad LoadMutated(Action<JsonObject> mutate)
    {
        using var fixture = RepositoryFixture.Copy();
        var node = JsonNode.Parse(fixture.Read(SourceFile))!.AsObject();
        mutate(node);
        fixture.Write(SourceFile, node.ToJsonString());
        return RuleSource.Load(fixture.Root);
    }

    private static void AssertFailsNaming(RuleSourceLoad load, string field)
    {
        Assert.Null(load.Source);
        Assert.Contains(load.Violations, violation =>
            violation.RuleId == "C2" && violation.Source == SourceFile && violation.Target.Contains(field, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRealSourceLoads()
    {
        var load = RuleSource.Load(RepositoryPaths.Root());

        Assert.Empty(load.Violations);
        Assert.NotNull(load.Source);
        Assert.Equal(2, load.Source.ContractVersion);
        Assert.Equal("specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md", load.Source.ContractPath);
        Assert.Equal("docs/03-domain-and-technical-architecture.md", load.Source.ModuleTablePath);
        Assert.Equal(12, load.Source.Modules.Count);
        Assert.Equal(10, load.Source.PortHomes.Count);
        Assert.Equal(34, load.Source.Rules.Count);
        Assert.All(load.Source.Rules, rule => Assert.False(rule.Retired));
        Assert.Equal("NU1900", Assert.Single(load.Source.Suppressions).Id);
    }

    [Fact]
    [ProvesRule("C2")]
    public void AMissingFileFails()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Delete(SourceFile);

        var load = RuleSource.Load(fixture.Root);

        Assert.Null(load.Source);
        var violation = Assert.Single(load.Violations);
        Assert.Equal(
            "C2: tests/Iskender.Architecture.Tests/module-dependency-rules.json -> (file): the rule source is missing. See tests/Iskender.Architecture.Tests/module-dependency-rules.json (version unknown).",
            violation.Message);
    }

    [Fact]
    [ProvesRule("C2")]
    public void MalformedJsonFails()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write(SourceFile, "{ not json");

        var load = RuleSource.Load(fixture.Root);

        Assert.Null(load.Source);
        Assert.Contains("not valid JSON", Assert.Single(load.Violations).Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("contract")]
    [InlineData("moduleTable")]
    [InlineData("modules")]
    [InlineData("portHomes")]
    [InlineData("projects")]
    [InlineData("testProjects")]
    [InlineData("requiredReferences")]
    [InlineData("forbiddenReach")]
    [InlineData("moduleNamespaces")]
    [InlineData("deniedPackages")]
    [InlineData("approvedSdks")]
    [InlineData("protectedSettings")]
    [InlineData("suppressions")]
    [InlineData("rules")]
    [ProvesRule("C2")]
    public void AMissingRequiredFieldFailsNamingIt(string field)
    {
        AssertFailsNaming(LoadMutated(node => node.Remove(field)), field);
    }

    [Theory]
    [InlineData("modules")]
    [InlineData("portHomes")]
    [InlineData("projects")]
    [InlineData("forbiddenReach")]
    [InlineData("approvedSdks")]
    [InlineData("suppressions")]
    [InlineData("rules")]
    [ProvesRule("C2")]
    public void AnEmptyRequiredListFailsNamingIt(string field)
    {
        AssertFailsNaming(LoadMutated(node => node[field] = new JsonArray()), field);
    }

    [Theory]
    [InlineData("requiredReferences")]
    [InlineData("deniedPackages")]
    [ProvesRule("C2")]
    public void AnEmptyRequiredObjectFailsNamingIt(string field)
    {
        AssertFailsNaming(LoadMutated(node => node[field] = new JsonObject()), field);
    }

    [Fact]
    [ProvesRule("C2")]
    public void AnEmptyStringFailsNamingIt()
    {
        AssertFailsNaming(LoadMutated(node => node["modules"]![0]!["namespaceRoot"] = ""), "modules[0].namespaceRoot");
    }

    [Fact]
    public void AllowListAndACategorysFrameworkReferencesAndSdksMayBeEmpty()
    {
        var load = LoadMutated(node =>
        {
            node["moduleNamespaces"]!["allowList"] = new JsonArray();
            node["deniedPackages"]!["aspnetcore"]!["frameworkReferences"] = new JsonArray();
            node["deniedPackages"]!["aspnetcore"]!["sdks"] = new JsonArray();
        });

        Assert.Empty(load.Violations);
        Assert.NotNull(load.Source);
    }

    [Fact]
    [ProvesRule("C2")]
    public void AnEmptyPrefixListFails()
    {
        AssertFailsNaming(LoadMutated(node => node["deniedPackages"]!["cloud"]!["prefixes"] = new JsonArray()), "deniedPackages.cloud.prefixes");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [ProvesRule("C2")]
    public void AContractVersionBelowTwoFails(int version)
    {
        AssertFailsNaming(LoadMutated(node => node["contract"]!["version"] = version), "contract.version");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AContractPathThatDoesNotExistFails()
    {
        AssertFailsNaming(LoadMutated(node => node["contract"]!["path"] = "absent/contract.md"), "contract.path");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AModuleTableFileThatDoesNotExistFails()
    {
        AssertFailsNaming(LoadMutated(node => node["moduleTable"]!["file"] = "absent.md"), "moduleTable.file");
    }

    [Fact]
    [ProvesRule("C2")]
    public void ADuplicateRuleIdFails()
    {
        AssertFailsNaming(LoadMutated(node => node["rules"]![1]!["id"] = node["rules"]![0]!["id"]!.GetValue<string>()), "rules[].id");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AnUnknownRoleFails()
    {
        AssertFailsNaming(LoadMutated(node => node["projects"]![0]!["role"] = "library"), "projects[0].role");
    }

    [Fact]
    [ProvesRule("C2")]
    public void ADeniedCategoryThatDoesNotExistFails()
    {
        AssertFailsNaming(LoadMutated(node => node["projects"]![0]!["denied"]![0] = "absent"), "projects[].denied");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AForbiddenReachRuleThatDoesNotExistFails()
    {
        AssertFailsNaming(LoadMutated(node => node["forbiddenReach"]![0]!["rule"] = "Z9"), "forbiddenReach[].rule");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AStarInForbiddenReachToFails()
    {
        AssertFailsNaming(LoadMutated(node => node["forbiddenReach"]![0]!["to"]![0] = "*"), "forbiddenReach[].to");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AnUnresolvedGroundingRootFails()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Replace(GroundingManifest.RelativePath, "  root: docs", "  absent: docs");

        var load = RuleSource.Load(fixture.Root);

        AssertFailsNaming(load, "grounding.root");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AnUnresolvedSpecificationsRootFails()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Replace(GroundingManifest.RelativePath, "  specificationsRoot: specs", "  absent: specs");

        var load = RuleSource.Load(fixture.Root);

        AssertFailsNaming(load, "speckit.specificationsRoot");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AMissingManifestFails()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Delete(GroundingManifest.RelativePath);

        var load = RuleSource.Load(fixture.Root);

        AssertFailsNaming(load, "grounding.root");
        AssertFailsNaming(load, "speckit.specificationsRoot");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AWrongFieldTypeFailsWithoutThrowing()
    {
        AssertFailsNaming(LoadMutated(node => node["modules"] = "not a list"), "modules");
    }

    [Fact]
    public void GroundingKeysResolveFromTheirOwnSection()
    {
        using var fixture = RepositoryFixture.Copy();
        Directory.Move(fixture.PathOf("docs"), fixture.PathOf("documentation"));
        fixture.Replace(GroundingManifest.RelativePath, "  root: docs", "  root: documentation");

        var load = RuleSource.Load(fixture.Root);

        Assert.Empty(load.Violations);
        Assert.Equal("documentation/03-domain-and-technical-architecture.md", load.Source!.ModuleTablePath);
    }

    [Theory]
    [InlineData("Iskender.Providers.Abstractions", "Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Providers.OpenAi", "Iskender.Providers.<Provider>")]
    [InlineData("Iskender.Infrastructure.Postgres", "Iskender.Infrastructure.<Adapter>")]
    [InlineData("Iskender.Infrastructure", "Iskender.Infrastructure")]
    public void AProjectNameResolvesToItsDefinition(string project, string definition)
    {
        Assert.Equal(definition, RuleSource.Load(RepositoryPaths.Root()).Source!.Resolve(project)?.Name);
    }

    [Theory]
    [InlineData("Iskender.Providers.Foo.Bar")]
    [InlineData("Iskender.Providers.")]
    [InlineData("Iskender.Unplanned")]
    [InlineData("iskender.domain")]
    public void AnUnknownNameResolvesToNothing(string project)
    {
        Assert.Null(RuleSource.Load(RepositoryPaths.Root()).Source!.Resolve(project));
    }

    [Fact]
    public void APatternNeverMatchesAnExactEntry()
    {
        Assert.False(RuleSource.Load(RepositoryPaths.Root()).Source!.Matches("Iskender.Providers.<Provider>", "Iskender.Providers.Abstractions"));
    }
}
