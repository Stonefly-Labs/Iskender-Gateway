using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Support;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>
/// The real run: the checkout against its merge base with <c>origin/main</c>, or <c>ISKENDER_COMPAT_BASE</c>. It must report
/// no violation, and it prints its declared breaks and deprecations (FR-034, FR-035, FR-039, SC-007).
/// </summary>
public class RepositoryHasNoViolationsTests(ITestOutputHelper output)
{
    [Fact]
    public void TheRepositoryHasNoViolations()
    {
        var result = HarnessRun.OnRepository(Repository.Root());
        output.WriteLine(result.Report());

        Assert.True(result.Passed, result.Report());
    }

    [Fact]
    public void TheRepositoryDeclaresNoBreakAndNoDeprecation()
    {
        var result = HarnessRun.OnRepository(Repository.Root());

        Assert.Empty(result.Notices);
    }
}
