using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class RepositoryScanTests
{
    private const string DomainProject = "src/Iskender.Domain/Iskender.Domain.csproj";

    private const string Infrastructure = "Iskender.Infrastructure";

    private static string[] RequiredSettings(string root) =>
        [.. RuleSource.Load(root).Source!.ProtectedSettings.Required.Select(setting => setting.Name)];

    private static ScannedProject Project(RepositoryScanResult scan, string name) =>
        scan.Projects.Single(project => project.Name == name);

    [Fact]
    public void FindsEveryProjectInTheRealRepository()
    {
        var scan = RepositoryScan.Run(RepositoryPaths.Root());

        Assert.Empty(scan.Problems);
        Assert.Contains(scan.Projects, project => project.Name == "Iskender.Gateway.Host" && !project.IsUnderTests);
        Assert.Contains(scan.Projects, project => project.Name == "Iskender.Architecture.Tests" && project.IsUnderTests);
        Assert.Equal(
            "src/Iskender.Domain/Iskender.Domain.csproj",
            Project(scan, "Iskender.Domain").Path);
    }

    [Fact]
    public void ReadsTheSolutionProjectsAsText()
    {
        var scan = RepositoryScan.Run(RepositoryPaths.Root());

        Assert.Contains(DomainProject, scan.SolutionProjects);
        Assert.DoesNotContain("src", scan.SolutionProjects);
        Assert.All(scan.Projects, project => Assert.Contains(project.Path, scan.SolutionProjects));
    }

    [Fact]
    public void FindsAProjectInAnyDirectory()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write("tools/Sneaky/Sneaky.proj", "<Project />");

        var project = Project(RepositoryScan.Run(fixture.Root), "Sneaky");

        Assert.Equal("tools/Sneaky/Sneaky.proj", project.Path);
        Assert.False(project.IsUnderTests);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("artifacts")]
    [InlineData("TestResults")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    public void SkipsOnlyTheSkippedDirectories(string directory)
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write($"src/{directory}/Hidden/Hidden.csproj", "<Project />");

        Assert.DoesNotContain(RepositoryScan.Run(fixture.Root).Projects, project => project.Name == "Hidden");
    }

    [Fact]
    public void OnlyAPathUnderTestsGivesTheTestRole()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write("src/Escaped.Tests/Escaped.Tests.csproj", "<Project />");
        fixture.Write("tests/Helper/Helper.csproj", "<Project />");

        var scan = RepositoryScan.Run(fixture.Root);

        Assert.False(Project(scan, "Escaped.Tests").IsUnderTests);
        Assert.True(Project(scan, "Helper").IsUnderTests);
    }

    [Fact]
    public void KeepsTwoFilesWithOneNameAsSeparateEntries()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write("tests/Other/Iskender.Domain.csproj", "<Project />");

        var entries = RepositoryScan.Run(fixture.Root).Projects.Where(project => project.Name == "Iskender.Domain").ToList();

        Assert.Equal(2, entries.Count);
        Assert.NotEqual(entries[0].Path, entries[1].Path);
    }

    [Fact]
    public void ReadsAReferenceWrittenWithBackslashesAndDifferentCasing()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(DomainProject, """<ItemGroup><ProjectReference Include="..\ISKENDER.infrastructure\Iskender.INFRASTRUCTURE.csproj" /></ItemGroup>""");

        var reference = Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain").References
            .Single(entry => entry.TargetName.Equals(Infrastructure, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(reference.ResolvedPath);
    }

    [Fact]
    public void ReadsAReferenceUnderANeverTrueCondition()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(DomainProject, """<ItemGroup Condition="'$(NeverTrue)' == 'true'"><ProjectReference Include="../Iskender.Infrastructure/Iskender.Infrastructure.csproj" /></ItemGroup>""");

        Assert.Contains(Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain").References, entry => entry.TargetName == Infrastructure);
    }

    [Fact]
    public void ReadsAReferenceInsideATarget()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(DomainProject, """<Target Name="Sneak"><ItemGroup><ProjectReference Include="../Iskender.Infrastructure/Iskender.Infrastructure.csproj" /></ItemGroup></Target>""");

        Assert.Contains(Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain").References, entry => entry.TargetName == Infrastructure);
    }

    [Fact]
    public void FollowsAnImportAndReadsWhatItAdds()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write("src/Iskender.Domain/extra.props", """<Project><ItemGroup><ProjectReference Include="../Iskender.Infrastructure/Iskender.Infrastructure.csproj" /></ItemGroup></Project>""");
        fixture.AddToProject(DomainProject, """<Import Project="$(MSBuildThisFileDirectory)extra.props" />""");

        var project = Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain");

        Assert.Contains(project.References, entry => entry.TargetName == Infrastructure && entry.DefinedIn == "src/Iskender.Domain/extra.props");
        Assert.Contains(project.Imports, import => import.ResolvedPath == "src/Iskender.Domain/extra.props");
    }

    [Fact]
    public void AnImportCycleEnds()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write("src/Iskender.Domain/a.props", """<Project><Import Project="b.props" /></Project>""");
        fixture.Write("src/Iskender.Domain/b.props", """<Project><Import Project="a.props" /></Project>""");
        fixture.AddToProject(DomainProject, """<Import Project="a.props" />""");

        Assert.Equal(2, Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain").Imports.Count(import => import.DefinedIn != DomainProject));
    }

    [Theory]
    [InlineData(RepositoryScan.RootBuildProps)]
    [InlineData(RepositoryScan.RootPackagesProps)]
    [InlineData(RepositoryScan.RootBuildTargets)]
    public void AReferenceInARootFileReachesEveryProject(string file)
    {
        using var fixture = RepositoryFixture.Copy();
        var content = """<ItemGroup Condition="'$(MSBuildProjectName)' == 'Nope'"><ProjectReference Include="$(MSBuildThisFileDirectory)src/Iskender.Infrastructure/Iskender.Infrastructure.csproj" /></ItemGroup>""";
        if (File.Exists(fixture.PathOf(file)))
        {
            fixture.AddToProject(file, content);
        }
        else
        {
            fixture.Write(file, $"<Project>{content}</Project>");
        }

        var scan = RepositoryScan.Run(fixture.Root);

        Assert.All(scan.Projects, project =>
            Assert.Contains(project.References, entry => entry.TargetName == Infrastructure && entry.DefinedIn == file && entry.ResolvedPath is not null));
    }

    [Theory]
    [InlineData(RepositoryScan.RootBuildProps)]
    [InlineData(RepositoryScan.RootPackagesProps)]
    [InlineData(RepositoryScan.RootBuildTargets)]
    public void APackageInARootFileReachesEveryProject(string file)
    {
        using var fixture = RepositoryFixture.Copy();
        var content = """<ItemGroup><PackageReference Include="Some.Package" /></ItemGroup>""";
        if (File.Exists(fixture.PathOf(file)))
        {
            fixture.AddToProject(file, content);
        }
        else
        {
            fixture.Write(file, $"<Project>{content}</Project>");
        }

        var scan = RepositoryScan.Run(fixture.Root);

        Assert.All(scan.Projects, project => Assert.Contains(project.PackageReferences, package => package.Value == "Some.Package"));
    }

    [Fact]
    public void AGlobalPackageReferenceCountsAsAPackage()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(RepositoryScan.RootPackagesProps, """<ItemGroup><GlobalPackageReference Include="Some.Global" Version="1.0.0" /></ItemGroup>""");

        Assert.All(RepositoryScan.Run(fixture.Root).Projects, project =>
            Assert.Contains(project.PackageReferences, package => package.Value == "Some.Global" && package.Type == "GlobalPackageReference"));
    }

    [Fact]
    public void ARequiredSettingInARootFileIsNotAProjectSetting()
    {
        var root = RepositoryPaths.Root();

        var scan = RepositoryScan.Run(root, RequiredSettings(root));

        Assert.All(scan.Projects, project => Assert.DoesNotContain(project.Properties, property => property.Name == "Nullable"));
        Assert.Contains(scan.RootFiles.Single(file => file.Name == RepositoryScan.RootBuildProps).Properties, property => property.Name == "Nullable");
    }

    [Fact]
    public void AForbiddenSettingInARootFileStillCounts()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(RepositoryScan.RootBuildProps, "<PropertyGroup><RunAnalyzers>false</RunAnalyzers></PropertyGroup>");

        var scan = RepositoryScan.Run(fixture.Root, RequiredSettings(fixture.Root));

        Assert.All(scan.Projects, project => Assert.Contains(project.Properties, property => property.Name == "RunAnalyzers"));
    }

    [Fact]
    public void ARequiredSettingInAProjectCounts()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(DomainProject, """<PropertyGroup Condition="'$(NeverTrue)' == 'true'"><Nullable>disable</Nullable></PropertyGroup>""");

        var scan = RepositoryScan.Run(fixture.Root, RequiredSettings(fixture.Root));

        var property = Project(scan, "Iskender.Domain").Properties.Single(entry => entry.Name == "Nullable");
        Assert.True(property.IsConditional);
    }

    [Fact]
    public void ReadsAnItemWhateverItsOperation()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(DomainProject, """<Target Name="Sneak"><ItemGroup><Analyzer Remove="@(Analyzer)" /><PackageReference Update="Some.Package" Version="1.0.0" /></ItemGroup></Target>""");

        var project = Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain");

        var analyzer = project.Items.Single(item => item.Type == "Analyzer");
        Assert.True(analyzer.InTarget);
        Assert.Equal("1.0.0", project.PackageReferences.Single(package => package.Value == "Some.Package").Metadata["Version"]);
    }

    [Fact]
    public void ReadsSdksFromEachElement()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(DomainProject, """<Sdk Name="Second.Sdk" Version="1.0.0" /><Import Project="Sdk.props" Sdk="Third.Sdk/2.0.0" />""");

        var names = Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain").Sdks.Select(sdk => sdk.Name).ToList();

        Assert.Equal(["Microsoft.NET.Sdk", "Second.Sdk", "Third.Sdk"], names);
    }

    [Fact]
    public void ReadsFrameworkAndAssemblyReferences()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(DomainProject, """<ItemGroup><FrameworkReference Include="Some.Framework" /><Reference Include="Some.Assembly" /></ItemGroup>""");

        var project = Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain");

        Assert.Equal("Some.Framework", Assert.Single(project.FrameworkReferences).Value);
        Assert.Equal("Some.Assembly", Assert.Single(project.AssemblyReferences).Value);
    }

    [Fact]
    public void ReadsLockFilePackagesExcludingProjects()
    {
        var scan = RepositoryScan.Run(RepositoryPaths.Root());

        var architecture = Project(scan, "Iskender.Architecture.Tests");
        var domain = Project(scan, "Iskender.Domain");

        Assert.True(domain.HasLockFile);
        Assert.Equal(
            ["Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions", "YamlDotNet"],
            domain.LockFilePackages.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("iskender.core.contracts", domain.LockFilePackages, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("xunit.v3", architecture.LockFilePackages);
        Assert.DoesNotContain("iskender.domain", architecture.LockFilePackages, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMissingLockFileIsRecorded()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Delete("src/Iskender.Domain/packages.lock.json");

        Assert.False(Project(RepositoryScan.Run(fixture.Root), "Iskender.Domain").HasLockFile);
    }

    [Fact]
    [ProvesRule("C2")]
    public void AnUnparseableProjectIsNamedAndTheScanContinues()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write(DomainProject, "<Project><unclosed>");

        var scan = RepositoryScan.Run(fixture.Root);

        Assert.Contains(scan.Problems, problem => problem.Source == DomainProject);
        Assert.Contains(scan.Projects, project => project.Name == "Iskender.Management");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AnUnparseableImportIsNamed()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write("src/Iskender.Domain/broken.props", "<Project><unclosed>");
        fixture.AddToProject(DomainProject, """<Import Project="broken.props" />""");

        Assert.Contains(RepositoryScan.Run(fixture.Root).Problems, problem => problem.Source == "src/Iskender.Domain/broken.props");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AnUnreadableLockFileIsNamed()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write("src/Iskender.Domain/packages.lock.json", "{ not json");

        Assert.Contains(RepositoryScan.Run(fixture.Root).Problems, problem => problem.Source == "src/Iskender.Domain/packages.lock.json");
    }

    [Fact]
    [ProvesRule("C2")]
    public void AMissingSolutionFileIsNamed()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Delete(RepositoryScan.SolutionFile);

        Assert.Contains(RepositoryScan.Run(fixture.Root).Problems, problem => problem.Source == RepositoryScan.SolutionFile);
    }

    [Fact]
    [ProvesRule("C2")]
    public void FindingNoProjectsIsAProblem()
    {
        var root = Directory.CreateTempSubdirectory("iskender-empty-");
        try
        {
            var scan = RepositoryScan.Run(root.FullName);

            Assert.Empty(scan.Projects);
            Assert.Contains(scan.Problems, problem => problem.Target == "project files");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
