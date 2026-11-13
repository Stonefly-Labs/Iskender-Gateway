using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis.Tests.Connections;

/// <summary>
/// FR-022: a script reply of a shape its script never returns fails inside the adapter's classification, as
/// <c>core.internal</c> with class <c>unexpected</c> and event 5010, never as a cast failure crossing the port.
/// </summary>
public class RedisReplyTests(RedisFixture fixture)
{
    private static readonly CoordinationName Name = new("fixture", "bad_reply");

    private readonly ConformanceScopes _scopes = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> BadReplies() => new()
    {
        // parser, Lua expression the bad script returns
        { "acquire", "'x'" },
        { "acquire", "{1, 2}" },
        { "acquire", "{1, 'x', 3}" },
        { "acquire", "{2, 1, 3}" },
        { "renew", "{1}" },
        { "flag", "{1}" },
        { "flag", "7" },
        { "integer", "'x'" },
        { "flagged_value", "{1, 2}" },
        { "flagged_value", "5" },
        { "value", "{1, 2, 3}" },
        { "value", "'x'" },
        { "lookup", "{1, 5}" },
        { "lookup", "{0, 'x'}" },
        { "lookup", "{1}" },
    };

    [Theory]
    [MemberData(nameof(BadReplies))]
    public async Task AnUnexpectedReplyShapeFailsWithInternalAndEvent5010(string parser, string reply)
    {
        fixture.RequireRedis();
        var logger = new CapturingDiagnostics.Logger();
        await using var connection = new RedisConnection(TestCoordinationSettings.For(fixture), TestCoordinationCredentialProvider.For(fixture), logger);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await RunAsync(connection, parser, reply));

        Assert.Same(ErrorCodes.Internal, failure.Code);
        Assert.Equal(CoordinationFailureClass.Unexpected, CoordinationFailures.ClassOf(failure));
        Assert.Null(failure.InnerException);
        Assert.Contains(logger.Events, logged => logged.Id.Id == 5010);
    }

    [Fact]
    public async Task AnUnexpectedCacheReplyIsAMiss()
    {
        fixture.RequireRedis();
        var logger = new CapturingDiagnostics.Logger();
        await using var connection = new RedisConnection(TestCoordinationSettings.For(fixture), TestCoordinationCredentialProvider.For(fixture), logger);

        var lookup = await CachePolicy.ReadOrMissAsync(logger, Name.Module, CoordinatorKind.Redis, () => BadAsync(connection, "{1, 5}", RedisVersionedCache.LookupReply), Token);

        Assert.False(lookup.IsHit);
        Assert.Null(lookup.Stamp);
    }

    [Fact]
    public void EveryParserReadsTheShapeItsScriptReturns()
    {
        Assert.Equal((true, 7L, 900L), RedisLeaseCoordinator.AcquireReply(Array(1, 7, 900)));
        Assert.Equal((false, 0L, 900L), RedisLeaseCoordinator.AcquireReply(Array(0, 0, 900)));
        Assert.Equal((true, 900L), RedisLeaseCoordinator.RenewReply(Array(1, 900)));
        Assert.Equal((true, new CoordinationValue(3, 4)), RedisAtomicOperations.FlaggedValueReply(Array(1, 3, 4)));
        Assert.Equal(new CoordinationValue(3, 4), RedisAtomicOperations.ValueReply(Array(3, 4)));
        Assert.Null(RedisAtomicOperations.ValueReply(RedisResult.Create(RedisValue.Null)));
        Assert.Equal(5, RedisVersionedCache.LookupReply(Array(0, 5)).Stamp!.Value.Generation);
        Assert.Equal([1, 2], RedisVersionedCache.LookupReply(RedisResult.Create([RedisResult.Create(1, ResultType.Integer), RedisResult.Create((RedisValue)new byte[] { 1, 2 }, ResultType.BulkString)])).Value.ToArray());
    }

    private ValueTask<object?> RunAsync(RedisConnection connection, string parser, string reply) => parser switch
    {
        "acquire" => Bad(connection, reply, RedisLeaseCoordinator.AcquireReply),
        "renew" => Bad(connection, reply, RedisLeaseCoordinator.RenewReply),
        "flag" => Bad(connection, reply, RedisReplies.Flag),
        "integer" => Bad(connection, reply, RedisReplies.Integer),
        "flagged_value" => Bad(connection, reply, RedisAtomicOperations.FlaggedValueReply),
        "value" => Bad(connection, reply, RedisAtomicOperations.ValueReply),
        "lookup" => Bad(connection, reply, RedisVersionedCache.LookupReply),
        _ => throw new ArgumentOutOfRangeException(nameof(parser)),
    };

    private async ValueTask<object?> Bad<T>(RedisConnection connection, string reply, Func<RedisResult, T> parse) =>
        await BadAsync(connection, reply, parse);

    // A script that returns reply, read by parse as a real script's reply would be.
    private ValueTask<T> BadAsync<T>(RedisConnection connection, string reply, Func<RedisResult, T> parse)
    {
        var script = new RedisScript("fixture.bad_reply", $"return {reply}", writes: false, inline: true);
        return connection.RunAsync(_scopes.OrganizationA, Name.Module, CoordinationPrimitive.AtomicOperations, script, [RedisScripts.ValueKey(_scopes.OrganizationA, Name)], [], parse, Token);
    }

    private static RedisResult Array(params long[] values) => RedisResult.Create(values.Select(value => RedisResult.Create(value, ResultType.Integer)).ToArray());
}
