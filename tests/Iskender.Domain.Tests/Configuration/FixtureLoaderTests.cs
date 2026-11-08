using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class FixtureLoaderTests
{
    [Fact]
    public void SidecarsSitBesideTheirFixtures()
    {
        Assert.Equal(
            Path.Combine("dir", "five-defects.expect.json"),
            FixtureLoader.SidecarOf(Path.Combine("dir", "five-defects.json")));
    }

    [Fact]
    public void FormatFollowsTheExtension()
    {
        Assert.Equal(ConfigurationDocumentFormat.Json, FixtureLoader.FormatOf("a/b.json"));
        Assert.Equal(ConfigurationDocumentFormat.Yaml, FixtureLoader.FormatOf("a/b.yaml"));
    }

}
