using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis.Tests.Connections;

public class RedisFailureClassifierTests
{
    private const string PlantedHost = "planted-host-7f3a.internal:6390";

    private const string ServerText = "server-text-91c2";

    public static TheoryData<string, string, string, string, bool, bool> Rows() => new()
    {
        // exception, stop, code, class, outcome unknown for a write, event 5003
        { "credential", "None", "coordination.unavailable", "authentication_failed", false, true },
        { "connection:AuthenticationFailure", "None", "coordination.unavailable", "authentication_failed", false, true },
        { "connection:UnableToConnect", "None", "coordination.unavailable", "unavailable", false, true },
        { "connection:UnableToResolvePhysicalConnection", "None", "coordination.unavailable", "unavailable", false, true },
        { "connection:SocketFailure", "None", "coordination.unavailable", "unavailable", true, true },
        { "connection:SocketClosed", "None", "coordination.unavailable", "unavailable", true, true },
        { "timeout", "None", "coordination.timeout", "timeout", true, false },
        { "server:NOAUTH", "None", "coordination.unavailable", "authentication_failed", false, true },
        { "server:WRONGPASS", "None", "coordination.unavailable", "authentication_failed", false, true },
        { "server:READONLY", "None", "coordination.unavailable", "unavailable", false, true },
        { "server:ERR", "None", "core.internal", "unexpected", false, false },
        { "server:ISK_RANGE", "None", "core.internal", "unexpected", false, false },
        { "other", "None", "core.internal", "unexpected", false, false },
        { "cancelled", "Cancelled", "coordination.cancelled", "cancelled", true, false },
        { "cancelled", "TimedOut", "coordination.timeout", "timeout", true, false },
        { "connection:SocketFailure", "Cancelled", "coordination.cancelled", "cancelled", true, false },
        { "timeout", "TimedOut", "coordination.timeout", "timeout", true, false },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void EveryClassificationRow(string exception, string stop, string code, string failureClass, bool unknownForWrite, bool connectionEvent)
    {
        foreach (var writes in new[] { true, false })
        {
            var logger = new CapturingDiagnostics.Logger();
            var script = writes ? RedisScripts.Add : RedisScripts.Read;

            var failure = RedisFailureClassifier.Classify(Build(exception), Enum.Parse<RedisStop>(stop), "fixture", CoordinationPrimitive.AtomicOperations, script, logger);

            Assert.Equal(code, failure.Code.Id);
            Assert.Equal(failureClass, CoordinationFailures.ClassOf(failure)!.Value.ToCode());
            Assert.Equal(writes && unknownForWrite, CoordinationFailures.IsOutcomeUnknown(failure));
            Assert.Null(failure.InnerException);
            Assert.Equal(writes && unknownForWrite, logger.Events.Any(log => log.Id.Id == 5004));
            Assert.Equal(connectionEvent, logger.Events.Any(log => log.Id.Id == 5003));
            Assert.Equal(failureClass == "unexpected", logger.Events.Any(log => log.Id.Id == 5010));
            foreach (var text in new[] { failure.Message, failure.ToString() }.Concat(failure.Error!.Diagnostics.Values.Select(pair => pair.Value)).Concat(logger.Events.Select(log => log.Message)))
            {
                Assert.DoesNotContain(PlantedHost, text, StringComparison.Ordinal);
                Assert.DoesNotContain(ServerText, text, StringComparison.Ordinal);
            }

            Assert.All(logger.Events, log => Assert.Null(log.Exception));
        }
    }

    [Theory]
    [InlineData("connection:AuthenticationFailure", true)]
    [InlineData("server:NOAUTH", true)]
    [InlineData("server:WRONGPASS", true)]
    [InlineData("credential", false)]
    [InlineData("connection:UnableToConnect", false)]
    [InlineData("server:READONLY", false)]
    [InlineData("timeout", false)]
    public void OnlyARefusedCredentialRebuildsTheConnection(string exception, bool rebuilds)
    {
        Assert.Equal(rebuilds, RedisFailureClassifier.IsAuthenticationFailure(Build(exception)));
    }

    private static Exception Build(string exception)
    {
        var detail = $"{ServerText} on {PlantedHost}";
        return exception switch
        {
            "credential" => new RedisCredentialUnavailableException(),
            "timeout" => new RedisTimeoutException(CommandFlags.None, $"Timeout awaiting response, {detail}", CommandStatus.Sent),
            "cancelled" => new OperationCanceledException(detail),
            "other" => new InvalidOperationException(detail),
            _ when exception.StartsWith("connection:", StringComparison.Ordinal) =>
                new RedisConnectionException(Enum.Parse<ConnectionFailureType>(exception["connection:".Length..]), CommandFlags.None, detail, new IOException(detail), CommandStatus.Unknown),
            _ when exception.StartsWith("server:", StringComparison.Ordinal) =>
                new RedisServerException(Kind(exception["server:".Length..]), CommandFlags.None, $"{exception["server:".Length..]} {detail}"),
            _ => throw new ArgumentOutOfRangeException(nameof(exception)),
        };
    }

    private static RedisErrorKind Kind(string prefix) => prefix switch
    {
        "NOAUTH" => RedisErrorKind.NoAuth,
        "WRONGPASS" => RedisErrorKind.WrongPass,
        "READONLY" => RedisErrorKind.ReadOnly,
        _ => RedisErrorKind.UnknownError,
    };
}
