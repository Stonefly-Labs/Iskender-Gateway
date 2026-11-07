using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class BuildConventionRuleTests
{
    private const string Domain = "src/Iskender.Domain/Iskender.Domain.csproj";

    private const string SourceFile = "src/Iskender.Domain/Added.cs";

    private static readonly RuleSource Source = RuleSource.Load(RepositoryPaths.Root()).Source!;

    public static TheoryData<string, string, string> EveryRequiredSetting
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var setting in Source.ProtectedSettings.Required)
            {
                data.Add(setting.Name, setting.Value, setting.File);
            }

            return data;
        }
    }

    public static TheoryData<string> EveryProtectedProperty
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Source.ProtectedSettings.Required.Select(setting => setting.Name).Concat(Source.ProtectedSettings.Forbidden))
            {
                data.Add(name.Replace("<Category>", "Security", StringComparison.Ordinal));
            }

            return data;
        }
    }

    public static TheoryData<string> EveryForbiddenProperty
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Source.ProtectedSettings.Forbidden)
            {
                data.Add(name.Replace("<Category>", "Security", StringComparison.Ordinal));
            }

            return data;
        }
    }

    public static TheoryData<string> EveryForbiddenItem
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Source.ProtectedSettings.ForbiddenItems)
            {
                data.Add(name);
            }

            return data;
        }
    }

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

    [Fact]
    public void TheRealRootFilesSetEveryRequiredValue()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId is "B3" or "B4");
    }

    [Theory]
    [MemberData(nameof(EveryRequiredSetting))]
    [ProvesRule("B3")]
    public void RemovingARequiredSettingFromItsRootFileFails(string name, string value, string file)
    {
        var violations = Run(fixture => fixture.Replace(file, $"<{name}>{value}</{name}>", string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "B3" && violation.Source == file && violation.Target == name);
    }

    [Theory]
    [MemberData(nameof(EveryRequiredSetting))]
    [ProvesRule("B3")]
    public void ChangingARequiredSettingInItsRootFileFails(string name, string value, string file)
    {
        var violations = Run(fixture => fixture.Replace(file, $"<{name}>{value}</{name}>", $"<{name}>weakened</{name}>"));

        Assert.Contains(violations, violation => violation.RuleId == "B3" && violation.Source == file && violation.Target == name && violation.Detail.Contains("weakened", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("B3")]
    public void ARequiredSettingRepeatedUnderAConditionInItsRootFileFails()
    {
        var violations = Run(fixture => fixture.AddToProject(RepositoryScan.RootBuildProps, """<PropertyGroup Condition="'$(NeverTrue)' == 'true'"><Nullable>enable</Nullable></PropertyGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B3" && violation.Target == "Nullable (count)");
        Assert.Contains(violations, violation => violation.RuleId == "B3" && violation.Target == "Nullable (condition)");
    }

    [Fact]
    [ProvesRule("B3")]
    public void AConditionOnTheOnlyRequiredSettingFails()
    {
        var violations = Run(fixture => fixture.Replace(RepositoryScan.RootBuildProps, "<Nullable>enable</Nullable>", """<Nullable Condition="'$(NeverTrue)' != 'true'">enable</Nullable>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B3" && violation.Target == "Nullable (condition)");
    }

    [Fact]
    [ProvesRule("B3")]
    public void AMissingRootFileFails()
    {
        var violations = Run(fixture => fixture.Delete(RepositoryScan.RootPackagesProps));

        Assert.Contains(violations, violation => violation.RuleId == "B3" && violation.Source == RepositoryScan.RootPackagesProps && violation.Target == "ManagePackageVersionsCentrally");
    }

    [Theory]
    [MemberData(nameof(EveryProtectedProperty))]
    [ProvesRule("B4")]
    public void SettingAProtectedPropertyInAProjectFails(string name)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, $"<PropertyGroup><{name}>x</{name}></PropertyGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == "Iskender.Domain" && violation.Target == name);
    }

    [Theory]
    [MemberData(nameof(EveryForbiddenItem))]
    [ProvesRule("B4")]
    public void AddingAForbiddenItemToAProjectFails(string name)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, $"""<ItemGroup><{name} Include="x" /></ItemGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == "Iskender.Domain" && violation.Target == name);
    }

    [Fact]
    [ProvesRule("B4")]
    public void NullableDisableUnderANeverTrueConditionFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<PropertyGroup Condition="'$(NeverTrue)' == 'true'"><Nullable>disable</Nullable></PropertyGroup>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == "Iskender.Domain" && violation.Target == "Nullable");
    }

    [Fact]
    [ProvesRule("B4")]
    public void NullableDisableInsideATargetFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<Target Name="Sneak"><PropertyGroup><Nullable>disable</Nullable></PropertyGroup></Target>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == "Iskender.Domain" && violation.Target == "Nullable");
    }

    [Fact]
    [ProvesRule("B4")]
    public void PropertyNamesCompareIgnoringCaseAsMsBuildDoes()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, "<PropertyGroup><nullable>disable</nullable></PropertyGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Target == "nullable");
    }

    [Theory]
    [MemberData(nameof(EveryForbiddenProperty))]
    [ProvesRule("B4")]
    public void AForbiddenSettingInTheRootPropsFails(string name)
    {
        var violations = Run(fixture => fixture.AddToProject(RepositoryScan.RootBuildProps, $"<PropertyGroup><{name}>x</{name}></PropertyGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == RepositoryScan.RootBuildProps && violation.Target == name);
    }

    [Fact]
    [ProvesRule("B4")]
    public void AForbiddenSettingInTheRootPackagesPropsFails()
    {
        var violations = Run(fixture => fixture.AddToProject(RepositoryScan.RootPackagesProps, "<PropertyGroup><RunAnalyzers>false</RunAnalyzers></PropertyGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == RepositoryScan.RootPackagesProps && violation.Target == "RunAnalyzers");
    }

    [Fact]
    [ProvesRule("B4")]
    public void AForbiddenSettingInTheRootTargetsFails()
    {
        var violations = Run(fixture => fixture.Write(RepositoryScan.RootBuildTargets, "<Project><PropertyGroup><RunAnalyzers>false</RunAnalyzers></PropertyGroup></Project>"));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == RepositoryScan.RootBuildTargets && violation.Target == "RunAnalyzers");
    }

    [Fact]
    [ProvesRule("B4")]
    public void ARequiredSettingInTheWrongRootFileFails()
    {
        var violations = Run(fixture => fixture.AddToProject(RepositoryScan.RootPackagesProps, "<PropertyGroup><Nullable>disable</Nullable></PropertyGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == RepositoryScan.RootPackagesProps && violation.Target == "Nullable");
    }

    [Fact]
    [ProvesRule("B4")]
    public void ARequiredSettingInTheRootTargetsFails()
    {
        var violations = Run(fixture => fixture.Write(RepositoryScan.RootBuildTargets, "<Project><PropertyGroup><Nullable>disable</Nullable></PropertyGroup></Project>"));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == RepositoryScan.RootBuildTargets && violation.Target == "Nullable");
    }

    [Fact]
    [ProvesRule("B4")]
    public void CustomAfterMicrosoftCommonTargetsNamingAFileFails()
    {
        var violations = Run(fixture =>
        {
            fixture.Write("src/Iskender.Domain/hidden.targets", "<Project><PropertyGroup><Nullable>disable</Nullable></PropertyGroup></Project>");
            fixture.AddToProject(Domain, "<PropertyGroup><CustomAfterMicrosoftCommonTargets>$(MSBuildThisFileDirectory)hidden.targets</CustomAfterMicrosoftCommonTargets></PropertyGroup>");
        });

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == "Iskender.Domain" && violation.Target == "CustomAfterMicrosoftCommonTargets");
    }

    [Fact]
    [ProvesRule("B4")]
    public void RemovingAnalyzersInsideATargetFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, """<Target Name="Sneak" BeforeTargets="CoreCompile"><ItemGroup><Analyzer Remove="@(Analyzer)" /></ItemGroup></Target>"""));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == "Iskender.Domain" && violation.Target == "Analyzer");
    }

    [Theory]
    [InlineData("src/Directory.Build.props")]
    [InlineData("src/Iskender.Domain/Directory.Build.targets")]
    [InlineData("tests/Directory.Packages.props")]
    [InlineData("src/.editorconfig")]
    [InlineData("Directory.Build.rsp")]
    [InlineData("MSBuild.rsp")]
    [InlineData(".globalconfig")]
    [InlineData("src/Iskender.Domain/.globalconfig")]
    [InlineData("tests/msbuild.rsp")]
    [ProvesRule("B4")]
    public void AConfigurationFileWhereItIsNotAllowedFails(string path)
    {
        var violations = Run(fixture => fixture.Write(path, "root = true"));

        Assert.Contains(violations, violation => violation.RuleId == "B4" && violation.Source == path);
    }

    [Fact]
    public void ARootBuildTargetsIsAllowed()
    {
        var violations = Run(fixture => fixture.Write(RepositoryScan.RootBuildTargets, "<Project />"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "B4");
    }

    [Fact]
    public void AnUnprotectedPropertyInAProjectPasses()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, "<PropertyGroup><Description>Fine.</Description></PropertyGroup>"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "B4");
    }

    [Fact]
    public void TheRealRepositoryHasNoUnlistedSuppression()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId is "B5" or "B6");
    }

    [Theory]
    [InlineData("#nullable disable")]
    [InlineData("# nullable disable")]
    [InlineData("    #nullable disable")]
    [InlineData("#  nullable   disable")]
    [InlineData("#nullable disable warnings")]
    [InlineData("#nullable disable annotations")]
    [ProvesRule("B5")]
    public void ANullableDirectiveFailsNamingTheFile(string directive)
    {
        var violations = Run(fixture => fixture.Write(SourceFile, $"namespace Iskender.Domain;\n{directive}\n"));

        Assert.Contains(violations, violation => violation.RuleId == "B5" && violation.Source == SourceFile);
    }

    [Theory]
    [InlineData("src/Iskender.Domain/Thing.g.cs")]
    [InlineData("src/Iskender.Domain/Thing.G.CS")]
    [InlineData("src/Iskender.Domain/Thing.g.i.cs")]
    [InlineData("src/Iskender.Domain/Thing.Designer.cs")]
    [InlineData("src/Iskender.Domain/Thing.generated.cs")]
    [InlineData("src/Iskender.Domain/TemporaryGeneratedFile_Thing.cs")]
    [InlineData("tests/Iskender.Architecture.Tests/Thing.g.cs")]
    [ProvesRule("B5")]
    public void AFileNamedAsGeneratedFails(string path)
    {
        var violations = Run(fixture => fixture.Write(path, "namespace Iskender.Domain;\n"));

        Assert.Contains(violations, violation => violation.RuleId == "B5" && violation.Source == path);
    }

    [Theory]
    [InlineData("// <auto-generated/>\nnamespace Iskender.Domain;\n")]
    [InlineData("// <autogenerated />\nnamespace Iskender.Domain;\n")]
    [InlineData("// <AUTO-GENERATED>\nnamespace Iskender.Domain;\n")]
    [InlineData("/* <auto-generated/> */\nnamespace Iskender.Domain;\n")]
    [InlineData("\n// A licence header.\n\n// <auto-generated/>\nnamespace Iskender.Domain;\n")]
    [InlineData("#nullable enable\n// <auto-generated/>\nnamespace Iskender.Domain;\n")]
    [ProvesRule("B5")]
    public void AGeneratedCommentBeforeTheFirstCodeFails(string text)
    {
        var violations = Run(fixture => fixture.Write(SourceFile, text));

        Assert.Contains(violations, violation => violation.RuleId == "B5" && violation.Source == SourceFile);
    }

    [Fact]
    public void AGeneratedCommentAfterTheFirstCodePasses()
    {
        var violations = Run(fixture => fixture.Write(SourceFile, "namespace Iskender.Domain;\n// <auto-generated/>\n"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "B5");
    }

    [Theory]
    [InlineData("<PropertyGroup><NoWarn>CS8618</NoWarn></PropertyGroup>")]
    [InlineData("<PropertyGroup><NoWarn>$(NoWarn);CS8618</NoWarn></PropertyGroup>")]
    [InlineData("<PropertyGroup><NoWarn>CS1591,CS8618</NoWarn></PropertyGroup>")]
    [InlineData("<PropertyGroup><WarningsNotAsErrors>CS8618</WarningsNotAsErrors></PropertyGroup>")]
    [InlineData("""<PropertyGroup Condition="'$(NeverTrue)' == 'true'"><NoWarn>CS8618</NoWarn></PropertyGroup>""")]
    [InlineData("""<Target Name="Sneak"><PropertyGroup><NoWarn>CS8618</NoWarn></PropertyGroup></Target>""")]
    [InlineData("""<ItemGroup><PackageReference Include="xunit.v3" NoWarn="CS8618" /></ItemGroup>""")]
    [InlineData("""<ItemGroup><PackageReference Include="xunit.v3"><NoWarn>CS8618</NoWarn></PackageReference></ItemGroup>""")]
    [InlineData("""<ItemGroup><PackageReference Include="xunit.v3" WarningsNotAsErrors="CS8618" /></ItemGroup>""")]
    [InlineData("""<ItemGroup><ProjectReference Include="../Iskender.Core.Contracts/Iskender.Core.Contracts.csproj" NoWarn="CS8618" /></ItemGroup>""")]
    [InlineData("""<ItemDefinitionGroup><PackageReference><NoWarn>CS8618</NoWarn></PackageReference></ItemDefinitionGroup>""")]
    [ProvesRule("B6")]
    public void ASuppressionInAProjectFileFailsNamingTheFile(string xml)
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, xml));

        Assert.Contains(violations, violation => violation.RuleId == "B6" && violation.Source == Domain && violation.Target == "CS8618");
    }

    [Fact]
    public void TheListedSuppressionInTheRootPropsPasses()
    {
        Assert.Contains(Source.Suppressions, listed => listed.Id == "NU1900");
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId == "B6" && violation.Target == "NU1900");
    }

    [Fact]
    [ProvesRule("B6")]
    public void AnUnlistedIdInTheRootPropsFails()
    {
        var violations = Run(fixture => fixture.Replace(RepositoryScan.RootBuildProps, "$(WarningsNotAsErrors);NU1900", "$(WarningsNotAsErrors);NU1900;CS8618"));

        Assert.Contains(violations, violation => violation.RuleId == "B6" && violation.Source == RepositoryScan.RootBuildProps && violation.Target == "CS8618");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "B6" && violation.Target == "NU1900");
    }

    [Fact]
    [ProvesRule("B6")]
    public void TheListedIdInAnotherSettingFails()
    {
        var violations = Run(fixture => fixture.AddToProject(RepositoryScan.RootBuildProps, "<PropertyGroup><NoWarn>NU1900</NoWarn></PropertyGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "B6" && violation.Source == RepositoryScan.RootBuildProps && violation.Target == "NU1900");
    }

    [Fact]
    [ProvesRule("B6")]
    public void TheListedIdInAProjectFails()
    {
        var violations = Run(fixture => fixture.AddToProject(Domain, "<PropertyGroup><WarningsNotAsErrors>NU1900</WarningsNotAsErrors></PropertyGroup>"));

        Assert.Contains(violations, violation => violation.RuleId == "B6" && violation.Source == Domain && violation.Target == "NU1900");
    }

    [Theory]
    [InlineData("#pragma warning disable CS8618", "CS8618")]
    [InlineData("# pragma warning disable CS8618", "CS8618")]
    [InlineData("    #pragma warning disable CS8618 // because", "CS8618")]
    [InlineData("#pragma warning disable CS1591, CS8618", "CS8618")]
    [InlineData("#pragma warning disable", "(all)")]
    [InlineData("""[SuppressMessage("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[SuppressMessage("Category", "CS8618:The name", Justification = "None")]""", "CS8618")]
    [InlineData("""[Fact, SuppressMessage("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[assembly: SuppressMessage("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[module: SuppressMessage("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[SuppressMessageAttribute("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[System.Diagnostics.CodeAnalysis.SuppressMessage("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[UnconditionalSuppressMessage("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[assembly: UnconditionalSuppressMessageAttribute("Category", "CS8618")]""", "CS8618")]
    [InlineData("""[SuppressMessage("Only one string")]""", "(unreadable)")]
    [InlineData("""[GeneratedCode("Tool", "1.0")]""", "GeneratedCode")]
    [InlineData("""[Fact, GeneratedCodeAttribute("Tool", "1.0")]""", "GeneratedCode")]
    [InlineData("""[System.CodeDom.Compiler.GeneratedCode("Tool", "1.0")]""", "GeneratedCode")]
    [ProvesRule("B6")]
    public void ASuppressionInASourceFileFailsNamingTheFile(string line, string id)
    {
        var violations = Run(fixture => fixture.Write(SourceFile, $"namespace Iskender.Domain;\n{line}\n"));

        Assert.Contains(violations, violation => violation.RuleId == "B6" && violation.Source == SourceFile && violation.Target == id);
    }

    [Fact]
    public void TheSameTextInOneLineStringsAndCommentsPasses()
    {
        var text = string.Join(
            '\n',
            "namespace Iskender.Domain;",
            "public static class Fine",
            "{",
            "    public const string A = \"#nullable disable\";",
            "    public const string B = \"#pragma warning disable CS8618\";",
            "    public const string C = @\"[SuppressMessage(\"\"Category\"\", \"\"CS8618\"\")]\";",
            "    public const string D = \"[GeneratedCode(\\\"Tool\\\", \\\"1.0\\\")]\";",
            "    // #nullable disable",
            "    // #pragma warning disable CS8618",
            "    // [SuppressMessage(\"Category\", \"CS8618\")]",
            "    // [GeneratedCode(\"Tool\", \"1.0\")]",
            "}",
            string.Empty);

        var violations = Run(fixture => fixture.Write(SourceFile, text));

        Assert.DoesNotContain(violations, violation => violation.RuleId is "B5" or "B6");
    }

    [Theory]
    [InlineData("dotnet_diagnostic.CA1305.severity = none", "CA1305")]
    [InlineData("dotnet_diagnostic.CA1305.severity = silent", "CA1305")]
    [InlineData("dotnet_diagnostic.CA1305.severity = suggestion", "CA1305")]
    [InlineData("dotnet_diagnostic.CA1305.severity = default", "CA1305")]
    [InlineData("dotnet_diagnostic.CA1305.severity=NONE", "CA1305")]
    [InlineData("  dotnet_diagnostic.CA1305.severity = none", "CA1305")]
    [InlineData("dotnet_analyzer_diagnostic.severity = none", "dotnet_analyzer_diagnostic")]
    [InlineData("dotnet_analyzer_diagnostic.category-Security.severity = silent", "dotnet_analyzer_diagnostic.category-Security")]
    [InlineData("generated_code = true", "generated_code")]
    [InlineData("generated_code=TRUE", "generated_code")]
    [ProvesRule("B6")]
    public void ASuppressionInTheEditorConfigFailsNamingTheFile(string line, string id)
    {
        var violations = Run(fixture => fixture.Write(".editorconfig", $"{fixture.Read(".editorconfig")}\n{line}\n"));

        Assert.Contains(violations, violation => violation.RuleId == "B6" && violation.Source == ".editorconfig" && violation.Target == id);
    }

    [Theory]
    [InlineData("dotnet_diagnostic.CA1305.severity = warning")]
    [InlineData("dotnet_diagnostic.CA1305.severity = error")]
    [InlineData("# dotnet_diagnostic.CA1305.severity = none")]
    [InlineData("; generated_code = true")]
    [InlineData("generated_code = false")]
    public void AnEditorConfigLineThatSuppressesNothingPasses(string line)
    {
        var violations = Run(fixture => fixture.Write(".editorconfig", $"{fixture.Read(".editorconfig")}\n{line}\n"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "B6");
    }
}
