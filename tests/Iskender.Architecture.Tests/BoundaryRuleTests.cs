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

    [Fact]
    [ProvesRule("A14")]
    public void AProjectOutsideBothDirectoriesFails()
    {
        var violations = Run(fixture => fixture.Write("tools/Iskender.Tool/Iskender.Tool.csproj", "<Project />"));

        Assert.Contains(violations, violation => violation.RuleId == "A14" && violation.Target == "tools/Iskender.Tool/Iskender.Tool.csproj");
        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Tool");
    }

    [Fact]
    [ProvesRule("A14")]
    public void ATestsNameUnderSrcFailsAndGainsNoExemption()
    {
        var violations = Run(fixture => fixture.Write("src/Iskender.Escaped.Tests/Iskender.Escaped.Tests.csproj", "<Project />"));

        Assert.Contains(violations, violation => violation.RuleId == "A14" && violation.Source == "Iskender.Escaped.Tests");
        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Escaped.Tests");
    }

    [Fact]
    [ProvesRule("A14")]
    public void ASecondProjectWithAnApprovedFileNameFails()
    {
        var violations = Run(fixture => fixture.Write("tests/Other/Iskender.Domain.csproj", "<Project />"));

        var paths = violations.Where(violation => violation.RuleId == "A14").Select(violation => violation.Target).ToList();
        Assert.Contains("tests/Other/Iskender.Domain.csproj", paths);
        Assert.Contains(Domain, paths);
    }

    [Fact]
    [ProvesRule("A14")]
    public void ASecondProjectDifferingOnlyInCaseFails()
    {
        var violations = Run(fixture => fixture.Write("tests/Other/ISKENDER.DOMAIN.csproj", "<Project />"));

        Assert.Contains(violations, violation => violation.RuleId == "A14" && violation.Target == "tests/Other/ISKENDER.DOMAIN.csproj");
    }

    [Fact]
    public void ASecondFileCannotBorrowTheExemption()
    {
        var violations = Run(fixture => fixture.Write(
            "tests/Other/Iskender.Domain.csproj",
            """<Project><ItemGroup><ProjectReference Include="../../src/Iskender.Infrastructure/Iskender.Infrastructure.csproj" /></ItemGroup></Project>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Infrastructure");
    }

    [Theory]
    [InlineData("AssemblyName")]
    [InlineData("RootNamespace")]
    [InlineData("PackageId")]
    [ProvesRule("A15")]
    public void AnIdentitySettingThatDiffersFromTheProjectNameFails(string setting)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, $"""<PropertyGroup Condition="'$(NeverTrue)' == 'true'"><{setting}>Something.Else</{setting}></PropertyGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A15" && violation.Source == "Iskender.Domain" && violation.Target == setting);
    }

    [Fact]
    public void TheRootRootNamespaceIsCompliant()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId == "A15");
    }

    [Fact]
    public void ATestProjectMayChooseItsOwnIdentity()
    {
        var violations = Run(fixture => fixture.AddToProject(
            "tests/Iskender.Core.Contracts.Tests/Iskender.Core.Contracts.Tests.csproj",
            "<PropertyGroup><AssemblyName>Something.Else</AssemblyName></PropertyGroup>"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A15");
    }

    [Fact]
    [ProvesRule("A16")]
    public void AnItemGrantToAProductionAssemblyFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><InternalsVisibleTo Include="Iskender.Infrastructure" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A16" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Infrastructure");
    }

    [Fact]
    [ProvesRule("A16")]
    public void AnAssemblyAttributeItemGrantToAProductionAssemblyFails()
    {
        var violations = Run(fixture => fixture.AddToProject(
            Domain,
            """<ItemGroup><AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo"><_Parameter1>Iskender.Infrastructure</_Parameter1></AssemblyAttribute></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A16" && violation.Target == "Iskender.Infrastructure");
    }

    [Theory]
    [InlineData("""[assembly: InternalsVisibleTo("Iskender.Infrastructure")]""")]
    [InlineData("""[assembly: System.Runtime.CompilerServices.InternalsVisibleToAttribute("Iskender.Infrastructure, PublicKey=00")]""")]
    [InlineData("""  [assembly: Foo, InternalsVisibleTo( "Iskender.Infrastructure" )]""")]
    [ProvesRule("A16")]
    public void ASourceGrantToAProductionAssemblyFails(string line)
    {
        var violations = Run(fixture => fixture.Write("src/Iskender.Domain/Friends.cs", line));

        Assert.Contains(violations, violation =>
            violation.RuleId == "A16" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Infrastructure" && violation.Detail.Contains("src/Iskender.Domain/Friends.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""<ItemGroup><InternalsVisibleTo Include="Iskender.Core.Contracts.Tests" /></ItemGroup>""", "")]
    [InlineData("", """[assembly: InternalsVisibleTo("Iskender.Core.Contracts.Tests")]""")]
    [InlineData("", """// [assembly: InternalsVisibleTo("Iskender.Infrastructure")]""")]
    [InlineData("", """var text = "[assembly: InternalsVisibleTo(Iskender.Infrastructure)]";""")]
    public void AGrantToATestProjectOrAMentionPasses(string item, string line)
    {
        var violations = Run(fixture =>
        {
            if (item.Length > 0)
            {
                fixture.AddToProject(Domain, item);
            }

            if (line.Length > 0)
            {
                fixture.Write("src/Iskender.Domain/Friends.cs", line);
            }
        });

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A16");
    }

    [Fact]
    public void ATestProjectGrantIsNotChecked()
    {
        var violations = Run(fixture => fixture.Write(
            "tests/Iskender.Core.Contracts.Tests/Friends.cs",
            """[assembly: InternalsVisibleTo("Iskender.Domain")]"""));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A16");
    }

    [Theory]
    [InlineData("""var a = "x"; // comment""", """var a =    ;           """)]
    [InlineData("""var a = @"C:\"; var b = "y";""", """var a = @     ; var b =    ;""")]
    [InlineData("""var a = "\""; var b = 1;""", """var a =     ; var b = 1;""")]
    [InlineData("""var a = '"'; var b = "z";""", """var a = '"'; var b =    ;""")]
    [InlineData(""""var a = """x""" + 1;"""", """var a =         + 1;""")]
    public void BlankingKeepsColumns(string line, string expected)
    {
        Assert.Equal(expected, SourceScan.Blank(line));
    }

    [Fact]
    public void AMultiLineRawStringIsNotBlanked()
    {
        var opening = "var a = " + new string('"', 3);

        Assert.Equal(opening, SourceScan.Blank(opening));
    }

    private static string PackageItem(string package) =>
        $"""<ItemGroup><PackageReference Include="{package}" /></ItemGroup>""";

    private static void WriteProject(RepositoryFixture fixture, string name, string content) =>
        fixture.Write($"src/{name}/{name}.csproj", $"""<Project Sdk="Microsoft.NET.Sdk">{content}</Project>""");

    private static void AddToLockFile(RepositoryFixture fixture, string project, string package) =>
        fixture.Replace(
            $"src/{project}/packages.lock.json",
            "\"net10.0\": {",
            $$"""
            "net10.0": { "{{package}}": { "type": "Transitive" },
            """);

    [Theory]
    [InlineData("Azure.Storage.Blobs")]
    [InlineData("AWSSDK.S3")]
    [InlineData("OpenAI")]
    [InlineData("Npgsql")]
    [InlineData("StackExchange.Redis")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Dapper")]
    [InlineData("Microsoft.AspNetCore.Mvc")]
    [InlineData("npgsql")]
    [ProvesRule("A19")]
    public void TheDomainTakingADeniedPackageFails(string package)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, PackageItem(package)));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Domain" && violation.Target == package);
    }

    [Fact]
    [ProvesRule("A19")]
    public void TheDomainTakingTheAspNetCoreFrameworkFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Domain" && violation.Target == "Microsoft.AspNetCore.App");
    }

    [Fact]
    [ProvesRule("A19")]
    public void TheDomainTakingTheWebSdkFails()
    {
        var violations = Run(fixture => fixture.Replace(Domain, """<Project Sdk="Microsoft.NET.Sdk">""", """<Project Sdk="Microsoft.NET.Sdk.Web">"""));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Domain" && violation.Target == "Microsoft.NET.Sdk.Web");
    }

    [Fact]
    [ProvesRule("A19")]
    public void ADeniedPackageArrivingOnlyThroughALockFileFails()
    {
        var violations = Run(fixture => AddToLockFile(fixture, "Iskender.Domain", "Azure.Core"));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Domain" && violation.Target == "Azure.Core");
    }

    [Fact]
    [ProvesRule("A19")]
    public void ADeniedGlobalPackageReferenceFailsForEveryProductionProject()
    {
        var violations = Run(fixture => fixture.AddToProject(RepositoryScan.RootPackagesProps, """<ItemGroup><GlobalPackageReference Include="Npgsql" Version="1.0.0" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Domain" && violation.Target == "Npgsql");
        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Gateway.Host" && violation.Target == "Npgsql");
        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.ControlPlane.Host" && violation.Target == "Npgsql");
    }

    [Fact]
    [ProvesRule("A19")]
    public void TheInfrastructureProjectTakingADriverFails()
    {
        var violations = Run(fixture => fixture.AddToProject("src/Iskender.Infrastructure/Iskender.Infrastructure.csproj", PackageItem("Npgsql")));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Infrastructure" && violation.Target == "Npgsql");
    }

    [Fact]
    [ProvesRule("A19")]
    public void TheManagementProjectTakingADataAccessLibraryFails()
    {
        var violations = Run(fixture => fixture.AddToProject("src/Iskender.Management/Iskender.Management.csproj", PackageItem("Microsoft.EntityFrameworkCore")));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Management" && violation.Target == "Microsoft.EntityFrameworkCore");
    }

    [Fact]
    [ProvesRule("A19")]
    public void AProviderAdapterTakingADataAccessLibraryFails()
    {
        var violations = Run(fixture => WriteProject(fixture, "Iskender.Providers.Sample", PackageItem("Dapper")));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Providers.Sample" && violation.Target == "Dapper");
    }

    [Fact]
    [ProvesRule("A19")]
    public void AHostTakingADriverDirectlyFails()
    {
        var violations = Run(fixture => fixture.AddToProject("src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj", PackageItem("Npgsql")));

        Assert.Contains(violations, violation => violation.RuleId == "A19" && violation.Source == "Iskender.Gateway.Host" && violation.Target == "Npgsql");
    }

    [Theory]
    [InlineData("Azureish.Tools")]
    [InlineData("Npgsql2")]
    [InlineData("OpenAIish")]
    public void APrefixLookAlikeDoesNotMatch(string package)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, PackageItem(package)));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A19");
    }

    [Fact]
    public void AProviderAdapterMayTakeACloudHostedProviderSdk()
    {
        var violations = Run(fixture => WriteProject(fixture, "Iskender.Providers.Sample", PackageItem("Azure.AI.OpenAI")));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A19");
    }

    [Fact]
    public void AnInfrastructureAdapterMayTakeADriver()
    {
        var violations = Run(fixture => WriteProject(fixture, "Iskender.Infrastructure.Sample", PackageItem("Npgsql")));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A19");
    }

    [Fact]
    public void AHostLockFileMayHoldAnAdaptersPackage()
    {
        var violations = Run(fixture => AddToLockFile(fixture, "Iskender.Gateway.Host", "Npgsql"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A19");
    }

    [Fact]
    public void ATestProjectIsExempt()
    {
        var violations = Run(fixture => fixture.AddToProject("tests/Iskender.Core.Contracts.Tests/Iskender.Core.Contracts.Tests.csproj", PackageItem("Npgsql")));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A19");
    }
}
