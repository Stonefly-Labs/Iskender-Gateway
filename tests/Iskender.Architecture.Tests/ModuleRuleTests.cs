using System.Reflection;
using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;
using NetArchTest.Rules;

namespace Iskender.Architecture.Tests;

public class ModuleRuleTests
{
    private const string Routing = "Iskender.Domain.Routing";

    private static readonly RuleSource Source = RuleSource.Load(RepositoryPaths.Root()).Source!;

    private static readonly Assembly Fixtures = Assembly.Load("Iskender.Architecture.Fixtures");

    private static string[] Reported(RuleSource source, string rule) =>
        [.. ModuleRules.Evaluate(source, Fixtures).Where(violation => violation.RuleId == rule).Select(violation => $"{violation.Source} -> {violation.Target}")];

    private static RuleSource WithAllowance(string from, string to) =>
        new()
        {
            ContractPath = Source.ContractPath,
            ContractVersion = Source.ContractVersion,
            ModuleTablePath = Source.ModuleTablePath,
            ModuleTableHeading = Source.ModuleTableHeading,
            Modules = Source.Modules,
            PortHomes = Source.PortHomes,
            Projects = Source.Projects,
            TestProjects = Source.TestProjects,
            RequiredReferences = Source.RequiredReferences,
            ForbiddenReach = Source.ForbiddenReach,
            ModuleNamespaces = new ModuleNamespaces(Source.ModuleNamespaces.Shared, [new ModuleAllowance(from, to)]),
            DeniedPackages = Source.DeniedPackages,
            ApprovedSdks = Source.ApprovedSdks,
            ProtectedSettings = Source.ProtectedSettings,
            Suppressions = Source.Suppressions,
            Rules = Source.Rules,
        };

    [Fact]
    [ProvesRule("A17")]
    public void A17ReportsExactlyTheTypesThatCrossAModule()
    {
        Assert.Equal(
            ["Iskender.Domain.Policy.Cel.ChildUsesRouting -> Iskender.Domain.Routing", "Iskender.Domain.Policy.UsesRouting -> Iskender.Domain.Routing"],
            Reported(Source, "A17"));
    }

    [Fact]
    [ProvesRule("A17")]
    public void A17NamesBothNamespaces()
    {
        var violation = ModuleRules.Evaluate(Source, Fixtures).First(entry => entry.Source == "Iskender.Domain.Policy.UsesRouting");

        Assert.Equal(
            $"A17: Iskender.Domain.Policy.UsesRouting -> {Routing}: Iskender.Domain.Policy may not use another module's namespace. See {Source.ContractPath} (version {Source.ContractVersion}).",
            violation.Message);
    }

    [Fact]
    public void A17HonorsTheAllowList()
    {
        Assert.Empty(Reported(WithAllowance("Iskender.Domain.Policy", Routing), "A17"));
    }

    [Fact]
    [ProvesRule("A18")]
    public void A18ReportsExactlyTheTypesOutsideEveryMappedNamespaceAndThePortUsingAModule()
    {
        Assert.Equal(
            [
                "Iskender.Domain.InRootNamespace -> Iskender.Domain",
                "Iskender.Domain.PolicyExtras.PrefixTrap -> Iskender.Domain.PolicyExtras",
                "Iskender.Domain.Ports.PortUsesModule -> Iskender.Domain.Routing",
                "Iskender.Domain.Unmapped.InUnmappedNamespace -> Iskender.Domain.Unmapped",
            ],
            Reported(Source, "A18"));
    }

    [Fact]
    public void ThePrefixTrapIsReportedAsUnmappedAndNotAsPolicy()
    {
        var violations = ModuleRules.Evaluate(Source, Fixtures);

        Assert.Contains(violations, violation => violation.RuleId == "A18" && violation.Target == "Iskender.Domain.PolicyExtras");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "A17" && violation.Source.Contains("PolicyExtras", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Iskender.Domain.Policy.UsesOnlyPorts")]
    [InlineData("Iskender.Domain.Policy.Cel.ChildUsesItsOwnModule")]
    [InlineData("Iskender.Domain.Ports.SharedShape")]
    [InlineData("Iskender.Domain.Routing.RouteTable")]
    public void ACleanTypeIsNotReported(string type)
    {
        Assert.DoesNotContain(ModuleRules.Evaluate(Source, Fixtures), violation => violation.Source == type);
    }

    [Fact]
    public void NetArchTestStillSelectsByPrefix()
    {
        var selected = Types.InAssembly(Fixtures).That().ResideInNamespace("Iskender.Domain.Policy").GetTypes().Select(type => type.FullName).ToList();

        Assert.Contains("Iskender.Domain.PolicyExtras.PrefixTrap", selected);
    }

    [Fact]
    public void TheRealDomainIsCleanButThatIsNotProof()
    {
        var domain = Assembly.Load("Iskender.Domain");

        Assert.Empty(ModuleRules.Evaluate(Source, domain).Select(violation => violation.Message));
    }
}
