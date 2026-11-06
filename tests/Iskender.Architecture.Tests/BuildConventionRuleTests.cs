using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class BuildConventionRuleTests
{
    private const string Domain = "src/Iskender.Domain/Iskender.Domain.csproj";

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
}
