using System.Diagnostics;
using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

/// <summary>
/// Builds, once, a small assembly outside the repository that breaks every rule the build-output check reads. It
/// stands in for <c>Iskender.Domain</c>, references an assembly named <c>Npgsql</c> and one named
/// <c>Iskender.Rogue</c>, grants <c>InternalsVisibleTo</c> to a production project, and turns nullable analysis off.
/// </summary>
public sealed class ViolatingAssembly : IDisposable
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(5);

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("iskender-assembly-");

    public ViolatingAssembly()
    {
        // Empty root files stop MSBuild and NuGet from reading anything above the temporary directory.
        Write("Directory.Build.props", "<Project />");
        Write("Directory.Build.targets", "<Project />");
        Write("nuget.config", "<configuration><packageSources><clear /></packageSources></configuration>");

        Library("Npgsql", "namespace Npgsql; public class Connection { public string Name { get; } = string.Empty; }");
        Library("Iskender.Rogue", "namespace Iskender.Rogue; public class Rogue { public string Name { get; } = string.Empty; }");
        Write("Violating/Violating.csproj", Project("<Nullable>disable</Nullable>", "<ItemGroup><ProjectReference Include=\"../Npgsql/Npgsql.csproj\" /><ProjectReference Include=\"../Iskender.Rogue/Iskender.Rogue.csproj\" /></ItemGroup>"));
        Write("Violating/Uses.cs", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Iskender.Hosting\")]\nnamespace Violating;\npublic static class Uses\n{\n    public static object Driver() => new Npgsql.Connection();\n    public static object Rogue() => new Iskender.Rogue.Rogue();\n}\n");

        Output = Build(System.IO.Path.Combine(directory.FullName, "Violating", "Violating.csproj"));
        Path = System.IO.Path.Combine(directory.FullName, "Violating", "bin", "Debug", "net10.0", "Violating.dll");
    }

    public string Output { get; }

    public string Path { get; }

    public void Dispose() => directory.Delete(recursive: true);

    private static string Project(string properties, string items = "") =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings>{properties}</PropertyGroup>{items}</Project>";

    private void Library(string name, string code)
    {
        Write($"{name}/{name}.csproj", Project("<Nullable>enable</Nullable>"));
        Write($"{name}/Code.cs", code);
    }

    private void Write(string relative, string text)
    {
        var path = System.IO.Path.Combine(directory.FullName, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Build(string project)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[] { "build", project, "-c", "Debug", "-nodeReuse:false" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(BuildTimeout))
        {
            process.Kill(entireProcessTree: true);
            return $"dotnet build did not finish within {BuildTimeout}.";
        }

        return $"exit code {process.ExitCode}\n{standardOutput.GetAwaiter().GetResult()}{standardError.GetAwaiter().GetResult()}";
    }
}

public class AssemblyRuleTests(ViolatingAssembly built) : IClassFixture<ViolatingAssembly>
{
    private const string Domain = "Iskender.Domain";

    private static readonly RuleSource Source = RuleSource.Load(RepositoryPaths.Root()).Source!;

    private static readonly RepositoryScanResult Scan = RepositoryScan.Run(RepositoryPaths.Root());

    private IReadOnlyList<Violation> Violations()
    {
        Assert.True(File.Exists(built.Path), built.Output);
        return AssemblyRules.Check(Source, Scan, [new BuiltAssembly(Domain, built.Path)], "Debug");
    }

    [Fact]
    public void TheBuiltProductionAssembliesHaveNoViolations()
    {
        var configuration = PackagingRules.Configuration();
        var assemblies = AssemblyRules.Locate(Scan, configuration);

        Assert.NotEmpty(assemblies);
        ViolationAssert.None(AssemblyRules.Check(Source, Scan, assemblies, configuration));
    }

    [Fact]
    [ProvesRule("C2")]
    public void AMissingAssemblyFailsAndSaysToBuildFirst()
    {
        var violation = Assert.Single(AssemblyRules.Check(Source, Scan, [new BuiltAssembly(Domain, "/nonexistent/Iskender.Domain.dll")], "Release"));

        Assert.Equal("C2", violation.RuleId);
        Assert.Contains("Build the solution first, in the Release configuration", violation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [ProvesRule("A15")]
    public void AnAssemblyNamedOtherThanItsProjectFails() =>
        Assert.Contains(Violations(), violation => violation.RuleId == "A15" && violation.Source == Domain && violation.Target == "Violating");

    [Fact]
    [ProvesRule("A13")]
    public void AnAssemblyReferenceToAnUnapprovedIskenderAssemblyFails() =>
        Assert.Contains(Violations(), violation => violation.RuleId == "A13" && violation.Source == Domain && violation.Target == "Iskender.Rogue");

    [Fact]
    [ProvesRule("A19")]
    public void AnAssemblyReferenceMatchingADeniedPrefixFails() =>
        Assert.Contains(Violations(), violation => violation.RuleId == "A19" && violation.Source == Domain && violation.Target == "Npgsql");

    [Fact]
    [ProvesRule("A16")]
    public void AnInternalsVisibleToGrantInTheAssemblyFails() =>
        Assert.Contains(Violations(), violation => violation.RuleId == "A16" && violation.Source == Domain && violation.Target == "Iskender.Hosting");

    [Fact]
    [ProvesRule("B4")]
    public void AnAssemblyBuiltWithNullableAnalysisOffFails() =>
        Assert.Contains(Violations(), violation => violation.RuleId == "B4" && violation.Source == Domain);

    [Fact]
    public void ALibraryBuiltWithNullableAnalysisOnPasses()
    {
        var library = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(built.Path)!, "Npgsql.dll");

        Assert.DoesNotContain(AssemblyRules.Check(Source, Scan, [new BuiltAssembly("Iskender.Core.Contracts", library)], "Debug"), violation => violation.RuleId == "B4");
    }
}
