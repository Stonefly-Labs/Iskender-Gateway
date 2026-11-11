using System.IO.Compression;
using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class PackagingRuleTests
{
    private const string Domain = "src/Iskender.Domain/Iskender.Domain.csproj";

    private const string GatewayHost = "src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj";

    private const string MarkPackable = "<PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>";

    private const string PackableLine = "<IsPackable>true</IsPackable>";

    private static readonly RuleSource Source = RuleSource.Load(RepositoryPaths.Root()).Source!;

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

    [Theory]
    [ProvesRule("P2")]
    [InlineData("""<ItemGroup><ProjectReference Include="../Iskender.Core.Contracts/Iskender.Core.Contracts.csproj" PrivateAssets="all" /></ItemGroup>""")]
    [InlineData("""<ItemGroup><ProjectReference Update="../Iskender.Core.Contracts/Iskender.Core.Contracts.csproj"><IncludeAssets>compile</IncludeAssets></ProjectReference></ItemGroup>""")]
    [InlineData("""<ItemDefinitionGroup><ProjectReference><ExcludeAssets>all</ExcludeAssets></ProjectReference></ItemDefinitionGroup>""")]
    public void APackableProjectReferenceWithAssetMetadataFails(string items)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, items));

        Assert.Contains(violations, violation => violation.RuleId == "P2" && violation.Source == "Iskender.Domain");
    }

    [Fact]
    public void ANonPackableProjectReferenceMayCarryAssetMetadata()
    {
        var violations = Run(fixture => fixture.AddToProject(GatewayHost, """<ItemGroup><ProjectReference Update="../Iskender.Hosting/Iskender.Hosting.csproj" PrivateAssets="all" /></ItemGroup>"""));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "P2");
    }

    [Fact]
    [ProvesRule("P3")]
    public void APackageOmittingAProjectReferenceFailsNamingBoth()
    {
        var scan = RepositoryScan.Run(RepositoryPaths.Root());
        var violations = Closure(
            directory =>
            {
                foreach (var project in scan.Projects.Where(project => !project.IsUnderTests && project.Properties.Any(property => property.Name == "IsPackable" && property.Value == "true")))
                {
                    var dependencies = project.Name == "Iskender.Hosting"
                        ? []
                        : project.References.Select(reference => reference.TargetName).ToArray();
                    WritePackage(directory, project.Name, dependencies);
                }
            },
            scan);

        Assert.Contains(violations, violation => violation.RuleId == "P3" && violation.Source == "Iskender.Hosting" && violation.Target == "Iskender.Domain");
    }

    [Fact]
    public void ANonPackableProjectMayReferenceANonPackableOne()
    {
        var violations = RuleRunner.Run(RepositoryPaths.Root());

        Assert.DoesNotContain(violations, violation => violation.RuleId == "P2" && violation.Source == "Iskender.Architecture.Tests");
    }

    private static void WritePackage(string directory, string id, params string[] dependencies)
    {
        var items = string.Concat(dependencies.Select(dependency => $"""<dependency id="{dependency}" version="1.0.0" />"""));
        var nuspec = $"""<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{id}</id><version>1.0.0</version><dependencies><group targetFramework="net10.0">{items}</group></dependencies></metadata></package>""";

        using var archive = ZipFile.Open(Path.Combine(directory, $"{id}.1.0.0.nupkg"), ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry($"{id}.nuspec").Open());
        writer.Write(nuspec);
    }

    private static IReadOnlyList<Violation> Closure(Action<string> arrange, RepositoryScanResult? scan = null)
    {
        var directory = Directory.CreateTempSubdirectory("iskender-pack-");
        try
        {
            arrange(directory.FullName);
            return PackagingRules.Closure(Source, directory.FullName, scan);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    [ProvesRule("P3")]
    public void APackageWithADanglingDependencyFailsNamingBoth()
    {
        var violations = Closure(directory =>
        {
            WritePackage(directory, "Iskender.Domain", "Iskender.Core.Contracts", "Iskender.Missing");
            WritePackage(directory, "Iskender.Core.Contracts");
        });

        var violation = Assert.Single(violations);
        Assert.Equal("P3", violation.RuleId);
        Assert.Equal("Iskender.Domain", violation.Source);
        Assert.Equal("Iskender.Missing", violation.Target);
    }

    [Fact]
    [ProvesRule("P3")]
    public void TwoDanglingDependenciesAreReportedInAStableOrder()
    {
        var violations = Closure(directory => WritePackage(directory, "Iskender.Domain", "Iskender.Zeta", "Iskender.Alpha"));

        Assert.Equal(["Iskender.Alpha", "Iskender.Zeta"], violations.Select(violation => violation.Target));
    }

    [Fact]
    [ProvesRule("P3")]
    public void AnEmptyPackOutputFails()
    {
        var violation = Assert.Single(Closure(_ => { }));

        Assert.Equal("P3", violation.RuleId);
    }

    [Fact]
    [ProvesRule("P3")]
    public void APackageWithNoNuspecFails()
    {
        var violations = Closure(directory =>
        {
            using var archive = ZipFile.Open(Path.Combine(directory, "Iskender.Domain.1.0.0.nupkg"), ZipArchiveMode.Create);
            archive.CreateEntry("readme.txt");
        });

        var violation = Assert.Single(violations);
        Assert.Equal("P3", violation.RuleId);
        Assert.Equal("nuspec", violation.Target);
    }

    [Fact]
    public void ACompleteClosurePassesAndOutsidePackagesAreIgnored()
    {
        var violations = Closure(directory =>
        {
            WritePackage(directory, "Iskender.Domain", "Iskender.Core.Contracts", "Some.Other.Package");
            WritePackage(directory, "Iskender.Core.Contracts");
        });

        Assert.Empty(violations);
    }

    [Fact]
    public void TheRealPackProducesTheEightPackagesWithAClosedSet()
    {
        var output = Directory.CreateTempSubdirectory("iskender-pack-");
        try
        {
            var pack = PackagingRules.Pack(RepositoryPaths.Root(), PackagingRules.Configuration(), output.FullName);
            Assert.True(pack.Succeeded, pack.Output);

            var produced = output.EnumerateFiles("*.nupkg").Select(file => file.Name).Order(StringComparer.Ordinal).ToList();
            string[] expected =
            [
                "Iskender.Core.Contracts", "Iskender.Domain", "Iskender.Hosting", "Iskender.Infrastructure",
                "Iskender.Infrastructure.PostgreSql", "Iskender.Management", "Iskender.Policy.Sdk", "Iskender.Providers.Abstractions",
            ];
            Assert.Equal(expected.Select(id => $"{id}.{PackagingRules.ClosureCheckVersion}.nupkg"), produced);
            ViolationAssert.None(PackagingRules.Closure(Source, output.FullName, RepositoryScan.Run(RepositoryPaths.Root())));
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    [Fact]
    public void MissingBuildOutputFailsAndSaysToBuildFirst()
    {
        using var fixture = RepositoryFixture.Copy();
        var output = fixture.PathOf("pack-output");

        var pack = PackagingRules.Pack(fixture.Root, PackagingRules.Configuration(), output);

        Assert.False(pack.Succeeded);
        Assert.Contains("Build the solution first", pack.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("error NU5026: The file 'Iskender.Domain.dll' to be packed was not found on disk.")]
    [InlineData("error NETSDK1004: Assets file 'project.assets.json' not found.")]
    public void AFailureShowingMissingBuildOutputSaysToBuildFirst(string printed)
    {
        var message = PackagingRules.PackFailure(1, "Release", printed);

        Assert.Contains("exit code 1", message, StringComparison.Ordinal);
        Assert.Contains("Build the solution first, in the Release configuration", message, StringComparison.Ordinal);
        Assert.Contains(printed, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1073741502, "")]
    [InlineData(1, "error MSB4018: The task failed unexpectedly.")]
    public void AnyOtherPackFailureReportsItsExitCodeAndOutputAlone(int exitCode, string printed)
    {
        var message = PackagingRules.PackFailure(exitCode, "Release", printed);

        Assert.Contains($"exit code {exitCode}", message, StringComparison.Ordinal);
        Assert.Contains(printed, message, StringComparison.Ordinal);
        Assert.DoesNotContain("Build the solution first", message, StringComparison.Ordinal);
    }
}
