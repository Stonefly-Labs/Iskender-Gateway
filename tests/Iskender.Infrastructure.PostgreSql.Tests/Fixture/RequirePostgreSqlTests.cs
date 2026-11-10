namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

public class RequirePostgreSqlTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("")]
    public void AvailableAlwaysRuns(string? ci)
    {
        Assert.Equal(RequirePostgreSql.Outcome.Run, RequirePostgreSql.Decide(unavailableReason: null, ci));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("1")]
    public void UnavailableWithoutCiSkips(string? ci)
    {
        Assert.Equal(RequirePostgreSql.Outcome.Skip, RequirePostgreSql.Decide("DockerUnavailableException: no runtime", ci));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void UnavailableInCiFails(string ci)
    {
        Assert.Equal(RequirePostgreSql.Outcome.Fail, RequirePostgreSql.Decide("DockerUnavailableException: no runtime", ci));
    }
}
