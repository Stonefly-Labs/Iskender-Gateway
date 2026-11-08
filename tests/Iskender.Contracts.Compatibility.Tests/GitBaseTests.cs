using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Support;

namespace Iskender.Contracts.Compatibility.Tests;

public class GitBaseTests
{
    private static readonly string Root = Repository.Root();

    [Fact]
    [ProvesRule("H1")]
    public void AnUnresolvableBaseFailsWithH1AndSaysToFetch()
    {
        var gitBase = GitBase.Resolve(Root, out var failure, requested: "does-not-exist");

        Assert.Null(gitBase);
        Assert.NotNull(failure);
        Assert.Equal("H1", failure.RuleId);
        Assert.Contains("ISKENDER_COMPAT_BASE=does-not-exist", failure.Message, StringComparison.Ordinal);
        Assert.Contains("git fetch origin main", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedBaseResolvesToItsCommit()
    {
        var gitBase = GitBase.Resolve(Root, out var failure, requested: "HEAD");

        Assert.Null(failure);
        Assert.NotNull(gitBase);
        Assert.Matches("^[0-9a-f]{40}$", gitBase.Commit);
        Assert.Equal(gitBase.Commit[..7], gitBase.Name);
    }

    [Fact]
    public void AFileAtTheBaseIsReadAndAMissingOneIsAbsent()
    {
        var gitBase = GitBase.Resolve(Root, out _, requested: "HEAD")!;

        Assert.Equal(File.ReadAllBytes(Path.Combine(Root, "global.json")), gitBase.Read("global.json"));
        Assert.Null(gitBase.Read("no/such/file.txt"));
    }

    [Fact]
    public void ADirectoryAtTheBaseIsListedAndAMissingOneIsEmpty()
    {
        var gitBase = GitBase.Resolve(Root, out _, requested: "HEAD")!;

        Assert.Contains(".github/workflows/ci.yml", gitBase.List(".github/workflows"));
        Assert.Empty(gitBase.List("no/such/directory"));
    }

    [Fact]
    public void TheRepositoryRootIsFoundByGit()
    {
        Assert.Equal(Path.GetFullPath(Root).TrimEnd('/'), Path.GetFullPath(GitBase.RepositoryRoot(Root)!).TrimEnd('/'));
    }
}
