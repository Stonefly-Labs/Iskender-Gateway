using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Support;
using Iskender.Contracts.Compatibility.Tests.Surface;

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

    /// <summary>
    /// The declared and deprecated notices are exactly the break record entries new since the base and the items newly
    /// obsolete in a committed baseline, so a change that declares a break or deprecates an item stays green (US1 scenario 5,
    /// FR-032, FR-039). Compatible changes are reported too, and <see cref="HarnessRunTests"/> checks them.
    /// </summary>
    [Fact]
    public void TheNoticesAreTheBreaksAndDeprecationsThisChangeDeclares()
    {
        var root = Repository.Root();
        var gitBase = GitBase.Resolve(root, out var failure);
        Assert.True(gitBase is not null, failure?.Message);
        var checkout = new DirectoryFileSet(root, "the checkout");

        var expected = BreakRecord.Parse(checkout.Read(RuleCatalog.BreakRecordPath))
            .Except(BreakRecord.Parse(gitBase.Read(RuleCatalog.BreakRecordPath)))
            .Select(entry => new Notice("declared", entry.Kind!, entry.Contract!, entry.Item!, entry.Reason!))
            .ToList();
        foreach (var path in checkout.List(HarnessRun.BaselineDirectory).Where(file => file.EndsWith(".surface.txt", StringComparison.Ordinal)))
        {
            var contract = Path.GetFileName(path)[..^".surface.txt".Length];
            var before = Lines(gitBase.ReadText(path)).Select(SurfaceLine.Parse).ToDictionary(line => line.Id, StringComparer.Ordinal);
            foreach (var line in Lines(checkout.ReadText(path)).Select(SurfaceLine.Parse))
            {
                if (line.Get("obsolete") is { } message && before.TryGetValue(line.Id, out var was) && was.Get("obsolete") is null)
                {
                    expected.Add(new Notice("deprecated", "V4", contract, line.Id, message));
                }
            }
        }

        var result = HarnessRun.OnRepository(root);

        Assert.Equal(
            expected.OrderBy(notice => notice.Kind, StringComparer.Ordinal)
                .ThenBy(notice => notice.Contract, StringComparer.Ordinal)
                .ThenBy(notice => notice.Item, StringComparer.Ordinal)
                .Select(notice => notice.ToString()),
            result.Notices.Where(notice => notice.Kind != Notice.Compatible).Select(notice => notice.ToString()));
    }

    private static string[] Lines(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
