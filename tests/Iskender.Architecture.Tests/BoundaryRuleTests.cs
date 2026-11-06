using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class BoundaryRuleTests
{
    private const string Domain = "src/Iskender.Domain/Iskender.Domain.csproj";

    private const string InfrastructureReference = """<ProjectReference Include="../Iskender.Infrastructure/Iskender.Infrastructure.csproj" />""";

    private static IReadOnlyList<Violation> Run(Action<RepositoryFixture> mutate)
    {
        using var fixture = RepositoryFixture.Copy();
        mutate(fixture);
        return RuleRunner.Run(fixture.Root);
    }

    private static void AddOrCreate(RepositoryFixture fixture, string file, string content)
    {
        if (File.Exists(fixture.PathOf(file)))
        {
            fixture.AddToProject(file, content);
        }
        else
        {
            fixture.Write(file, $"<Project>{content}</Project>");
        }
    }

    [Fact]
    [ProvesRule("A13")]
    public void AReferenceUnderANeverTrueConditionIsSeen()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, $"""<ItemGroup Condition="'$(NeverTrue)' == 'true'">{InfrastructureReference}</ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Infrastructure");
    }

    [Fact]
    [ProvesRule("A13")]
    public void AReferenceInAnImportedFileIsSeen()
    {
        var violations = Run(fixture =>
        {
            fixture.Write("src/Iskender.Domain/extra.props", $"<Project><ItemGroup>{InfrastructureReference}</ItemGroup></Project>");
            fixture.AddToProject(Domain, """<Import Project="extra.props" />""");
        });

        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Infrastructure");
    }

    [Fact]
    [ProvesRule("A13")]
    public void AReferenceConditionedOnTheProjectNameInTheRootPropsIsSeen()
    {
        var violations = Run(fixture => fixture.AddToProject(
            RepositoryScan.RootBuildProps,
            """<ItemGroup Condition="'$(MSBuildProjectName)' == 'Iskender.Management'"><ProjectReference Include="$(MSBuildThisFileDirectory)src/Iskender.Infrastructure/Iskender.Infrastructure.csproj" /></ItemGroup>"""));

        foreach (var project in new[] { "Iskender.Management", "Iskender.Domain", "Iskender.Core.Contracts" })
        {
            Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == project && violation.Target == "Iskender.Infrastructure");
        }

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Gateway.Host" && violation.Target == "Iskender.Infrastructure");
    }

    [Theory]
    [InlineData(RepositoryScan.RootBuildProps)]
    [InlineData(RepositoryScan.RootPackagesProps)]
    [InlineData(RepositoryScan.RootBuildTargets)]
    [ProvesRule("A13")]
    public void ADanglingImportInARootFileFails(string file)
    {
        var violations = Run(fixture => AddOrCreate(fixture, file, """<Import Project="absent.props" Condition="false" />"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Source == file && violation.Target == "absent.props");
    }

    [Fact]
    [ProvesRule("A13")]
    public void AnAssemblyReferenceToAnIskenderProjectFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><Reference Include="Iskender.Infrastructure, Version=1.0.0.0" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Source == "Iskender.Domain");
    }

    [Fact]
    [ProvesRule("A13")]
    public void AnAssemblyReferenceHidingBehindAHintPathFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><Reference Include="Innocent"><HintPath>..\out\Iskender.Infrastructure.dll</HintPath></Reference></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Target == "Innocent");
    }

    [Fact]
    public void AnUnrelatedAssemblyReferencePassesA13()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><Reference Include="Some.Other.Assembly" /></ItemGroup>"""));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A13");
    }

    [Fact]
    [ProvesRule("A13")]
    public void APackageNamedLikeAnIskenderProjectFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><PackageReference Include="Iskender.Infrastructure" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Infrastructure");
    }

    [Theory]
    [InlineData("""<ProjectReference Include="$(Sneaky)/Iskender.Infrastructure.csproj" />""", "$(Sneaky)/Iskender.Infrastructure.csproj")]
    [InlineData("""<ProjectReference Include="@(Sneaky)" />""", "@(Sneaky)")]
    [ProvesRule("A13")]
    public void AReferenceWithAnUnresolvedPropertyFails(string element, string target)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, $"<ItemGroup>{element}</ItemGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Target == target && violation.Detail.Contains("cannot resolve", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("A13")]
    public void AnImportWithAnUnresolvedPropertyFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<Import Project="$(Sneaky)/extra.props" />"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Target == "$(Sneaky)/extra.props");
    }

    [Fact]
    public void TheTwoKnownPropertiesResolve()
    {
        var violations = Run(fixture =>
        {
            fixture.Write("src/Iskender.Domain/a.props", "<Project />");
            fixture.Write("src/Iskender.Domain/b.props", "<Project />");
            fixture.AddToProject(Domain, """<Import Project="$(MSBuildThisFileDirectory)a.props" /><Import Project="$(MSBuildProjectDirectory)/b.props" />""");
        });

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A13");
    }

    [Theory]
    [InlineData("../Absent/Absent.csproj")]
    [InlineData("../../../outside/Outside.csproj")]
    [InlineData("../**/*.csproj")]
    [ProvesRule("A13")]
    public void ADanglingReferenceFails(string include)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, $"""<ItemGroup><ProjectReference Include="{include}" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Source == "Iskender.Domain" && violation.Target == include);
    }

    [Fact]
    [ProvesRule("A13")]
    public void AReferenceToAProjectInASkippedDirectoryFails()
    {
        var violations = Run(fixture =>
        {
            fixture.Write("artifacts/Hidden/Hidden.csproj", "<Project />");
            fixture.AddToProject(Domain, """<ItemGroup><ProjectReference Include="../../artifacts/Hidden/Hidden.csproj" /></ItemGroup>""");
        });

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Target == "../../artifacts/Hidden/Hidden.csproj");
    }

    [Fact]
    [ProvesRule("A13")]
    public void AnUnapprovedSdkOnTheProjectElementFails()
    {
        var violations = Run(fixture => fixture.Replace(Domain, """<Project Sdk="Microsoft.NET.Sdk">""", """<Project Sdk="Sneaky.Sdk/1.0.0">"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Source == "Iskender.Domain" && violation.Target == "Sneaky.Sdk");
    }

    [Fact]
    [ProvesRule("A13")]
    public void AnUnapprovedSdkOnAnSdkElementFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<Sdk Name="Sneaky.Sdk" />"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Target == "Sneaky.Sdk");
    }

    [Fact]
    [ProvesRule("A13")]
    public void AnUnapprovedSdkOnAnImportFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<Import Project="Sdk.targets" Sdk="Sneaky.Sdk" />"""));

        Assert.Contains(violations, violation => violation.RuleId == "A13" && violation.Target == "Sneaky.Sdk");
    }

    [Fact]
    [ProvesRule("A13")]
    public void ASolutionEntryUnderASkippedDirectoryFails()
    {
        var violations = Run(fixture =>
        {
            fixture.Write("artifacts/Hidden/Hidden.csproj", "<Project />");
            var entry = """Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Hidden", "artifacts\Hidden\Hidden.csproj", "{00000000-0000-0000-0000-000000000001}" """;
            fixture.Write(RepositoryScan.SolutionFile, fixture.Read(RepositoryScan.SolutionFile) + entry + "\r\nEndProject\r\n");
        });

        Assert.Contains(violations, violation =>
            violation.RuleId == "A13" && violation.Source == RepositoryScan.SolutionFile && violation.Target == "artifacts/Hidden/Hidden.csproj");
    }
}
