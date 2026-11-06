using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class BuildConventionRuleTests
{
    private const string Domain = "src/Iskender.Domain/Iskender.Domain.csproj";

    private static IReadOnlyList<Violation> Run(Action<RepositoryFixture> mutate)
    {
        using var fixture = RepositoryFixture.Copy();
        mutate(fixture);
        return RuleRunner.Run(fixture.Root);
    }

    [Theory]
    [InlineData("""<PackageReference Include="Some.Package" Version="1.0.0" />""")]
    [InlineData("""<PackageReference Include="Some.Package"><Version>1.0.0</Version></PackageReference>""")]
    [InlineData("""<PackageReference Update="Some.Package" Version="1.0.0" />""")]
    [ProvesRule("B1")]
    public void AVersionInAProjectFailsNamingTheProjectAndPackage(string element)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, $"<ItemGroup>{element}</ItemGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "B1" && violation.Source == "Iskender.Domain" && violation.Target == "Some.Package");
    }

    [Fact]
    [ProvesRule("B1")]
    public void AVersionUnderANeverTrueConditionFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup Condition="'$(NeverTrue)' == 'true'"><PackageReference Include="Some.Package" Version="1.0.0" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B1" && violation.Target == "Some.Package");
    }

    [Fact]
    [ProvesRule("B1")]
    public void APackageReferenceWithAVersionInTheRootPropsFails()
    {
        var violations = Run(fixture => fixture.AddToProject(RepositoryScan.RootBuildProps, """<ItemGroup><PackageReference Include="Some.Package" Version="1.0.0" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B1" && violation.Source == RepositoryScan.RootBuildProps && violation.Target == "Some.Package");
    }

    [Fact]
    [ProvesRule("B1")]
    public void AVersionOverrideFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><PackageReference Include="Some.Package" VersionOverride="1.0.0" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B1" && violation.Source == "Iskender.Domain" && violation.Target == "Some.Package");
    }

    [Fact]
    [ProvesRule("B1")]
    public void DisablingCentralManagementInAProjectFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<PropertyGroup Condition="'$(NeverTrue)' == 'true'"><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B1" && violation.Source == "Iskender.Domain" && violation.Target == "ManagePackageVersionsCentrally");
    }

    [Fact]
    [ProvesRule("B1")]
    public void AGlobalPackageReferenceWithAVersionOutsideTheRootFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><GlobalPackageReference Include="Some.Package" Version="1.0.0" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B1" && violation.Target == "Some.Package");
    }

    [Fact]
    public void AGlobalPackageReferenceWithAVersionInTheRootPasses()
    {
        var violations = Run(fixture => fixture.AddToProject(RepositoryScan.RootPackagesProps, """<ItemGroup><GlobalPackageReference Include="Some.Package" Version="1.0.0" /></ItemGroup>"""));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "B1");
    }

    [Fact]
    [ProvesRule("B2")]
    public void AMissingLockFileFails()
    {
        var violations = Run(fixture => fixture.Delete("src/Iskender.Domain/packages.lock.json"));

        Assert.Contains(violations, violation => violation.RuleId == "B2" && violation.Source == "Iskender.Domain" && violation.Target == "packages.lock.json");
    }
}
