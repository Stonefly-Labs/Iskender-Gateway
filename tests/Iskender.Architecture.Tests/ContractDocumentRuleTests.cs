using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class ContractDocumentRuleTests
{
    private const string CodeOwners = ".github/CODEOWNERS";

    private const string CiWorkflow = ".github/workflows/ci.yml";

    private const string DocsGate = ".github/scripts/docs-gate.sh";

    private const string Contributing = "CONTRIBUTING.md";

    private static IReadOnlyList<Violation> Run(Action<RepositoryFixture> mutate)
    {
        using var fixture = RepositoryFixture.Copy();
        mutate(fixture);
        return RuleRunner.Run(fixture.Root);
    }

    [Fact]
    public void TheRealRepositoryIsCovered()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId is "C3");
    }

    [Theory]
    [InlineData("/tests/Iskender.Architecture.Tests/", "tests/Iskender.Architecture.Tests/module-dependency-rules.json")]
    [InlineData("/.editorconfig", ".editorconfig")]
    [InlineData("/Directory.Build.props", "Directory.Build.props")]
    [InlineData("/Directory.Packages.props", "Directory.Packages.props")]
    [InlineData("/Directory.Build.targets", "Directory.Build.targets")]
    [InlineData("/src/Iskender.Core.Contracts/", "src/Iskender.Core.Contracts/Iskender.Core.Contracts.csproj")]
    [InlineData("/src/Iskender.Providers.Abstractions/", "src/Iskender.Providers.Abstractions/Iskender.Providers.Abstractions.csproj")]
    [InlineData("/src/Iskender.Policy.Sdk/", "src/Iskender.Policy.Sdk/Iskender.Policy.Sdk.csproj")]
    [InlineData("/src/Iskender.Domain/", "src/Iskender.Domain/Iskender.Domain.csproj")]
    [InlineData("/src/Iskender.Infrastructure/", "src/Iskender.Infrastructure/Iskender.Infrastructure.csproj")]
    [InlineData("/src/Iskender.Management/", "src/Iskender.Management/Iskender.Management.csproj")]
    [InlineData("/src/Iskender.Hosting/", "src/Iskender.Hosting/Iskender.Hosting.csproj")]
    [InlineData("/src/Iskender.Gateway.Host/", "src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj")]
    [InlineData("/src/Iskender.ControlPlane.Host/", "src/Iskender.ControlPlane.Host/Iskender.ControlPlane.Host.csproj")]
    [ProvesRule("C3")]
    public void RemovingACoverEntryFailsNamingThePath(string pattern, string path)
    {
        var violations = Run(fixture => fixture.Replace(CodeOwners, $"{pattern} @christopherhouse", string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Source == CodeOwners && violation.Target == path);
    }

    [Fact]
    [ProvesRule("C3")]
    public void ANewProjectWithNoCoverFails()
    {
        const string Project = "src/Iskender.Providers.Sample/Iskender.Providers.Sample.csproj";

        var violations = Run(fixture => fixture.Write(Project, "<Project />"));

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Target == Project);
    }

    [Fact]
    [ProvesRule("C3")]
    public void ALaterEntryWithNoOwnerTakesCoverAway()
    {
        var violations = Run(fixture => fixture.Write(CodeOwners, $"{fixture.Read(CodeOwners)}\n/src/Iskender.Domain/\n"));

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Target == "src/Iskender.Domain/Iskender.Domain.csproj");
    }

    [Fact]
    [ProvesRule("C3")]
    public void ACommentedOutEntryDoesNotCover()
    {
        var violations = Run(fixture => fixture.Replace(CodeOwners, "/.editorconfig @christopherhouse", "# /.editorconfig @christopherhouse"));

        Assert.Contains(violations, violation => violation.RuleId == "C3" && violation.Target == ".editorconfig");
    }

    [Theory]
    [InlineData("* @christopherhouse")]
    [InlineData("/** @christopherhouse")]
    [InlineData("*.csproj @christopherhouse")]
    [InlineData("/src/*/*.csproj @christopherhouse")]
    public void AWiderPatternCovers(string entry)
    {
        var violations = Run(fixture => fixture.Write(CodeOwners, $"{fixture.Read(CodeOwners).Replace("/src/Iskender.Domain/ @christopherhouse", string.Empty, StringComparison.Ordinal)}\n{entry}\n"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "C3" && violation.Target == "src/Iskender.Domain/Iskender.Domain.csproj");
    }

    [Fact]
    public void AMissingOwnersFileIsAnUnreadableInput()
    {
        var violations = Run(fixture => fixture.Delete(CodeOwners));

        Assert.Contains(violations, violation => violation.RuleId == "C2" && violation.Source == CodeOwners);
    }

    [Fact]
    public void TheRealCiAndDocumentationGateCoverEveryProject()
    {
        Assert.DoesNotContain(RuleRunner.Run(RepositoryPaths.Root()), violation => violation.RuleId is "C4");
    }

    [Theory]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("C4")]
    public void AnExecutableMissingFromTheAttestationFails(string name)
    {
        var violations = Run(fixture => fixture.Replace(CiWorkflow, $"            src/{name}/bin/${{{{ env.CONFIGURATION }}}}/net10.0/{name}.dll\n", string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Source == CiWorkflow && violation.Target == name && violation.Detail.Contains("subject-path", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("C4")]
    public void AnExecutableMissingFromTheChecksumStepFails(string name)
    {
        var violations = Run(fixture => fixture.Replace(CiWorkflow, $"\"src/{name}/bin/$CONFIGURATION/net10.0/{name}.dll\"", string.Empty));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Source == CiWorkflow && violation.Target == name && violation.Detail.Contains("sha256sum", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("C4")]
    public void APathOnlyInACommentDoesNotCountAsAttested()
    {
        const string Path = "src/Iskender.ControlPlane.Host/bin/${{ env.CONFIGURATION }}/net10.0/Iskender.ControlPlane.Host.dll";

        var violations = Run(fixture => fixture.Replace(CiWorkflow, Path, $"# {Path}"));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Target == "Iskender.ControlPlane.Host");
    }

    [Theory]
    [InlineData("Iskender.Core.Contracts")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [InlineData("Iskender.Hosting")]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("C4")]
    public void ABehaviorBearingPathMissingFromTheGateScriptFails(string name)
    {
        var violations = Run(fixture => fixture.Replace(DocsGate, $"src/{name}/*", "src/Removed/*"));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Source == DocsGate && violation.Target == name);
    }

    [Theory]
    [InlineData("Iskender.Core.Contracts")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [InlineData("Iskender.Hosting")]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("C4")]
    public void ABehaviorBearingPathMissingFromContributingFails(string name)
    {
        var violations = Run(fixture => fixture.Replace(Contributing, $"`src/{name}/**`", "`src/Removed/**`"));

        Assert.Contains(violations, violation => violation.RuleId == "C4" && violation.Source == Contributing && violation.Target == name);
    }

    [Theory]
    [InlineData(CiWorkflow)]
    [InlineData(DocsGate)]
    [InlineData(Contributing)]
    public void AMissingInputIsUnreadable(string path)
    {
        var violations = Run(fixture => fixture.Delete(path));

        Assert.Contains(violations, violation => violation.RuleId == "C2" && violation.Source == path);
    }
}
