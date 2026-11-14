using Iskender.Contracts.Compatibility.Fixtures.Reader;
using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Support;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>H5, H6, and H7 fail on their compiled fixtures, and the real production projects pass (FR-011, FR-017, FR-021, US4 scenarios 3 and 5).</summary>
public class DeclarationRuleTests
{
    private static readonly DirectoryFileSet Checkout = new(Repository.Root(), "the checkout");

    private static IEnumerable<Type> Fixtures(string rule) =>
        typeof(Sample).Assembly.GetTypes().Where(type => type.Namespace == $"Iskender.Contracts.Compatibility.Fixtures.Declarations.{rule}");

    private static List<string> Describe(IEnumerable<Violation> violations) =>
        violations.Select(violation => $"{violation.RuleId} {violation.Item}: {violation.WhatChanged}").Order(StringComparer.Ordinal).ToList();

    private static readonly IReadOnlyList<Violation> RealRun = DeclarationRules.Check(Checkout);

    [Fact]
    public void TheProductionProjectsAreEveryProjectThatIsNeitherAnExecutableNorASample()
    {
        Assert.Equal(
            ["Iskender.Core.Contracts", "Iskender.Domain", "Iskender.Hosting", "Iskender.Infrastructure", "Iskender.Management", "Iskender.Policy.Sdk", "Iskender.Providers.Abstractions"],
            DeclarationRules.ProductionProjects(Checkout));
    }

    [Fact]
    [ProvesRule("H5")]
    public void ARegistryInstanceDeclaredOutsideItsRegistryFailsH5()
    {
        const string Type = "Iskender.Contracts.Compatibility.Fixtures.Declarations.H5.SecondRegistry";

        Assert.Equal(
            [
                $"H5 {Type}::static field Malformed: declares a ErrorCode outside its registry",
                $"H5 {Type}::static property Organization: declares a ResourceType outside its registry",
            ],
            Describe(DeclarationRules.H5(Fixtures("H5"), "the checkout")));
        Assert.DoesNotContain(RealRun, violation => violation.RuleId == "H5");
    }

    [Fact]
    [ProvesRule("H5")]
    public void ConstructingARegistryInstanceOutsideItsRegistryFailsH5()
    {
        var violation = Assert.Single(DeclarationRules.H5([typeof(Constructs)], "the checkout"));

        Assert.Equal(("H5", "creates a ErrorCode outside its registry"), (violation.RuleId, violation.WhatChanged));
    }

    [Fact]
    [ProvesRule("H6")]
    public void AnUnlistedOwnershipResolverFailsH6()
    {
        Assert.Equal(
            ["H6 type Iskender.Contracts.Compatibility.Fixtures.Declarations.H6.RogueResolver: derives from OwnershipResolver and is not in the contract's H6 table"],
            Describe(DeclarationRules.H6(Fixtures("H6"), RuleCatalog.OwnershipResolverAllowList, "the checkout")));
        Assert.Empty(DeclarationRules.H6(Fixtures("H6"), ["Iskender.Contracts.Compatibility.Fixtures.Declarations.H6.RogueResolver"], "the checkout"));
        Assert.DoesNotContain(RealRun, violation => violation.RuleId == "H6");
    }

    [Fact]
    [ProvesRule("H7")]
    public void AContextMemberExposingADeniedTypeFailsH7()
    {
        const string Type = "Iskender.Contracts.Compatibility.Fixtures.Declarations.H7.LeakyView";

        Assert.Equal(
            [
                $"H7 {Type}::Open: exposes System.IO.Stream",
                $"H7 {Type}::Services: exposes System.IServiceProvider",
                $"H7 {Type}::Use: exposes Iskender.Contracts.Compatibility.Fixtures.Declarations.H7.ApiCredential",
            ],
            Describe(DeclarationRules.H7(Fixtures("H7"), "the checkout")));
        Assert.DoesNotContain(RealRun, violation => violation.RuleId == "H7");
    }

    [Fact]
    public void TheProductionProjectsPassH5ToH7()
    {
        Assert.Empty(RealRun);
    }

    /// <summary>Creates an <c>ErrorCode</c> in a method body, which only a project with internals access can do.</summary>
    private static class Constructs
    {
        public static Core.Contracts.Errors.ErrorCode Make() =>
            new Core.Contracts.Errors.ErrorCode("fixture.made_here", Core.Contracts.Errors.ErrorCategory.Internal, "Made outside the registry.", "An internal error occurred.", Core.Contracts.Versioning.RegistryStatus.Active, 1);
    }
}
