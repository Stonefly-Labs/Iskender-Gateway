namespace Iskender.Domain.Tests.Configuration;

public class FixtureLayoutTests
{
    [Fact]
    public void FixturesAreCopiedToTheTestOutput()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Configuration", "Fixtures", "README.md")));
    }
}
