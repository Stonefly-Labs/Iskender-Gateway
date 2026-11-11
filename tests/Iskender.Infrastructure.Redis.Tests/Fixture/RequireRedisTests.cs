using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis.Tests.Fixture;

public class RequireRedisTests(RedisFixture fixture)
{
    [Theory]
    [InlineData(null, null, "Run")]
    [InlineData(null, "true", "Run")]
    [InlineData("no docker", null, "Skip")]
    [InlineData("no docker", "", "Skip")]
    [InlineData("no docker", "false", "Skip")]
    [InlineData("no docker", "true", "Fail")]
    [InlineData("no docker", "TRUE", "Fail")]
    public void WithoutAContainerATestIsSkippedUnlessCiIsTrue(string? unavailableReason, string? ci, string expected)
    {
        Assert.Equal(expected, RequireRedis.Decide(unavailableReason, ci).ToString());
    }

    [Fact]
    public async Task TheAdapterUserCanPingAndReachOnlyItsKeyPattern()
    {
        fixture.RequireRedis();
        var options = fixture.OptionsFor(fixture.Username, fixture.Password);
        options.CommandMap = CommandMap.Create(["INFO", "CONFIG", "CLUSTER", "ECHO", "SUBSCRIBE", "UNSUBSCRIBE", "CLIENT", "SENTINEL"], available: false);
        options.ConfigurationChannel = "";
        options.TieBreaker = "";
        options.SetClientLibrary = false;
        options.Protocol = RedisProtocol.Resp3;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var database = connection.GetDatabase();

        Assert.True(connection.IsConnected);
        Assert.True((await database.PingAsync()) >= TimeSpan.Zero);
        Assert.True((await database.StringGetAsync("isk:v1:fixture:{probe}:v")).IsNull);
        var denied = await Assert.ThrowsAnyAsync<RedisException>(() => database.StringGetAsync("outside"));
        Assert.Contains("NOPERM", denied.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDefaultUserIsDisabled()
    {
        fixture.RequireRedis();
        var admin = await fixture.AdminAsync();

        var users = (string[])(await admin.ExecuteAsync("ACL", "LIST"))!;

        Assert.Contains(users, user => user.StartsWith("user default off ", StringComparison.Ordinal));
        Assert.Contains(users, user => user.StartsWith($"user {fixture.Username} on ", StringComparison.Ordinal)
            && user.EndsWith("~isk:v1:* resetchannels -@all +evalsha +eval +script|load +ping +role +hello +get +set +del +incr +pexpire +pttl +hget +hset +hmget +time", StringComparison.Ordinal));
    }
}
