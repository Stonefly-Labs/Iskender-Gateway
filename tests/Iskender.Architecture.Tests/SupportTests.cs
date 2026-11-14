using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class SupportTests
{
    private static readonly string[] Document =
    [
        "# Title",
        "## First",
        "Some prose.",
        "",
        "| A | B |",
        "|---|---|",
        "| 1 | `2` |",
        "| 3 | 4 |",
        "",
        "More prose.",
        "",
        "| C |",
        "|---|",
        "| 5 |",
        "",
        "### Child",
        "",
        "| D |",
        "|---|",
        "| 6 |",
        "",
        "## Second",
        "No table here.",
        "",
        "## `Quoted` heading",
        "| E |",
        "|---|",
        "| 7 |",
    ];

    [Fact]
    public void MarkdownTableReturnsNullForAMissingHeading()
    {
        Assert.Null(MarkdownTable.UnderHeading(Document, "Absent"));
    }

    [Fact]
    public void MarkdownTableReturnsNothingForAHeadingWithNoTable()
    {
        Assert.Empty(MarkdownTable.UnderHeading(Document, "Second")!);
    }

    [Fact]
    public void MarkdownTableReturnsEveryTableUnderAHeadingInOrder()
    {
        var tables = MarkdownTable.UnderHeading(Document, "First")!;

        Assert.Equal(3, tables.Count);
        Assert.Equal(["A", "B"], tables[0].Header);
        Assert.Equal(["1", "`2`"], tables[0].Rows[0]);
        Assert.Equal(["3", "4"], tables[0].Rows[1]);
        Assert.Equal(["5"], tables[1].Rows[0]);
        Assert.Equal(["6"], tables[2].Rows[0]);
    }

    [Fact]
    public void MarkdownTableStopsAtAHeadingOfTheSameLevel()
    {
        var tables = MarkdownTable.UnderHeading(Document, "Child")!;

        Assert.Equal(["6"], Assert.Single(tables).Rows[0]);
    }

    [Fact]
    public void MarkdownTableMatchesAHeadingWithoutItsBackticks()
    {
        Assert.Single(MarkdownTable.UnderHeading(Document, "Quoted heading")!);
    }

    [Fact]
    public void FixtureCopiesEveryInputARuleReads()
    {
        using var fixture = RepositoryFixture.Copy();

        foreach (var relative in new[]
        {
            "Iskender.slnx",
            "Directory.Build.props",
            "Directory.Packages.props",
            "global.json",
            ".editorconfig",
            ".iskender/grounding.yaml",
            ".github/CODEOWNERS",
            ".github/workflows/ci.yml",
            ".github/scripts/docs-gate.sh",
            "CONTRIBUTING.md",
            "docs/03-domain-and-technical-architecture.md",
            "specs/0006-host-composition-di-registration/contracts/module-dependency-rules.md",
            "tests/Iskender.Architecture.Tests/module-dependency-rules.json",
            "tests/Iskender.Architecture.Tests/Iskender.Architecture.Tests.csproj",
            "tests/Iskender.Architecture.Tests/packages.lock.json",
            "tests/Iskender.Architecture.Tests/SupportTests.cs",
            "src/Iskender.Domain/Iskender.Domain.csproj",
            "src/Iskender.Gateway.Host/Program.cs",
        })
        {
            Assert.True(File.Exists(fixture.PathOf(relative)), relative);
        }
    }

    [Fact]
    public void FixtureLeavesBuildOutputBehind()
    {
        using var fixture = RepositoryFixture.Copy();

        var copied = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories)
            .Select(path => RepositoryPaths.Relative(fixture.Root, path))
            .ToList();

        Assert.DoesNotContain(copied, path => path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal));
    }

    [Fact]
    public void FixtureDeletesItsDirectoryAfterward()
    {
        string root;
        using (var fixture = RepositoryFixture.Copy())
        {
            root = fixture.Root;
            Assert.True(Directory.Exists(root));
        }

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void FixtureMutationDoesNotTouchTheRealRepository()
    {
        var real = File.ReadAllText(Path.Combine(RepositoryPaths.Root(), "Directory.Build.props"));
        using var fixture = RepositoryFixture.Copy();

        fixture.Replace("Directory.Build.props", "<Nullable>enable</Nullable>", "<Nullable>disable</Nullable>");

        Assert.Equal(real, File.ReadAllText(Path.Combine(RepositoryPaths.Root(), "Directory.Build.props")));
        Assert.Contains("<Nullable>disable</Nullable>", fixture.Read("Directory.Build.props"), StringComparison.Ordinal);
    }

    [Fact]
    public void FixtureReplaceFailsOnTextThatIsNotThere()
    {
        using var fixture = RepositoryFixture.Copy();

        Assert.Throws<InvalidOperationException>(() => fixture.Replace("Directory.Build.props", "absent text", "x"));
    }

    [Fact]
    public void GroundingManifestReadsAKeyFromItsSection()
    {
        var root = RepositoryPaths.Root();

        Assert.Equal("docs", GroundingManifest.Read(root, "grounding", "root"));
        Assert.Equal("specs", GroundingManifest.Read(root, "speckit", "specificationsRoot"));
        Assert.Null(GroundingManifest.Read(root, "speckit", "root"));
        Assert.Null(GroundingManifest.Read(root, "grounding", "absent"));
    }
}
