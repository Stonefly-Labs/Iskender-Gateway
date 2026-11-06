using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class PackagingRuleTests
{
    private const string Domain = "src/Iskender.Domain/Iskender.Domain.csproj";

    private const string GatewayHost = "src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj";

    private const string MarkPackable = "<PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>";

    private const string PackableLine = "<IsPackable>true</IsPackable>";

    private static IReadOnlyList<Violation> Run(Action<RepositoryFixture> mutate)
    {
        using var fixture = RepositoryFixture.Copy();
        mutate(fixture);
        return RuleRunner.Run(fixture.Root);
    }

    [Fact]
    public void TheRealProjectsAreCompliant()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId is "P1" or "P2");
    }

    [Theory]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("P1")]
    public void AHostMarkedPackableFails(string host)
    {
        var violations = Run(fixture => fixture.AddToProject($"src/{host}/{host}.csproj", MarkPackable));

        Assert.Contains(violations, violation => violation.RuleId == "P1" && violation.Source == host && violation.Target == "IsPackable");
    }

    [Fact]
    [ProvesRule("P1")]
    public void ATestProjectMarkedPackableFails()
    {
        var violations = Run(fixture => fixture.AddToProject("tests/Iskender.Core.Contracts.Tests/Iskender.Core.Contracts.Tests.csproj", MarkPackable));

        Assert.Contains(violations, violation => violation.RuleId == "P1" && violation.Source == "Iskender.Core.Contracts.Tests");
    }

    [Theory]
    [InlineData("Iskender.Hosting")]
    [InlineData("Iskender.Domain")]
    [InlineData("Iskender.Infrastructure")]
    [InlineData("Iskender.Management")]
    [InlineData("Iskender.Core.Contracts")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [ProvesRule("P1")]
    public void APackableRoleNotMarkedPackableFails(string project)
    {
        var violations = Run(fixture => fixture.Replace($"src/{project}/{project}.csproj", PackableLine, string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "P1" && violation.Source == project && violation.Target == "IsPackable");
    }

    [Fact]
    [ProvesRule("P1")]
    public void AConditionalIsPackableFails()
    {
        var violations = Run(fixture => fixture.Replace(Domain, PackableLine, """<IsPackable Condition="'$(NeverTrue)' != 'true'">true</IsPackable>"""));

        Assert.Contains(violations, violation => violation.RuleId == "P1" && violation.Source == "Iskender.Domain" && violation.Detail.Contains("no condition", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("P1")]
    public void AConditionalIsPackableOnANonPackableRoleFails()
    {
        var violations = Run(fixture => fixture.AddToProject(GatewayHost, """<PropertyGroup Condition="'$(NeverTrue)' == 'true'"><IsPackable>true</IsPackable></PropertyGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "P1" && violation.Source == "Iskender.Gateway.Host");
    }

    [Fact]
    [ProvesRule("P1")]
    public void IsPackableInAnImportedFileFails()
    {
        var violations = Run(fixture =>
        {
            fixture.Write("src/Iskender.Gateway.Host/shared.props", $"<Project>{MarkPackable}</Project>");
            fixture.AddToProject(GatewayHost, """<Import Project="shared.props" />""");
        });

        Assert.Contains(violations, violation => violation.RuleId == "P1" && violation.Source == "src/Iskender.Gateway.Host/shared.props");
    }

    [Fact]
    [ProvesRule("P1")]
    public void TurningOnPackingByDefaultFails()
    {
        var violations = Run(fixture => fixture.Replace(RepositoryScan.RootBuildProps, "<IsPackable>false</IsPackable>", PackableLine));

        Assert.Contains(violations, violation => violation.RuleId == "P1" && violation.Source == RepositoryScan.RootBuildProps);
    }

    [Fact]
    [ProvesRule("P1")]
    public void AProjectWithNoRoleFails()
    {
        var violations = Run(fixture => fixture.Write("src/Iskender.Unlisted/Iskender.Unlisted.csproj", "<Project />"));

        Assert.Contains(violations, violation => violation.RuleId == "P1" && violation.Source == "Iskender.Unlisted" && violation.Detail.Contains("no role", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("P2")]
    public void APackableProjectReferencingANonPackableOneFailsNamingBoth()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><ProjectReference Include="../Iskender.Gateway.Host/Iskender.Gateway.Host.csproj" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "P2" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Gateway.Host");
    }

    [Fact]
    [ProvesRule("P2")]
    public void APackableProjectLosingAPackableTargetFails()
    {
        var violations = Run(fixture => fixture.Replace("src/Iskender.Core.Contracts/Iskender.Core.Contracts.csproj", PackableLine, string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "P2" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Core.Contracts");
    }

    [Fact]
    public void ANonPackableProjectMayReferenceANonPackableOne()
    {
        var violations = RuleRunner.Run(RepositoryPaths.Root());

        Assert.DoesNotContain(violations, violation => violation.RuleId == "P2" && violation.Source == "Iskender.Architecture.Tests");
    }
}
