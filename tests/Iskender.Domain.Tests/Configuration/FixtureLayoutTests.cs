namespace Iskender.Domain.Tests.Configuration;

public class FixtureLayoutTests
{
    private static readonly string[] FixtureFolders = ["valid", "invalid", "edge"];

    [Fact]
    public void FixturesAreCopiedToTheTestOutput()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Configuration", "Fixtures", "README.md")));
    }

    [Fact]
    public void NoTwoFixturesShareASidecar()
    {
        var fixtures = FixtureFolders.SelectMany(FixtureLoader.Fixtures).ToList();

        Assert.Empty(fixtures.GroupBy(FixtureLoader.SidecarOf, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key));
    }
}
