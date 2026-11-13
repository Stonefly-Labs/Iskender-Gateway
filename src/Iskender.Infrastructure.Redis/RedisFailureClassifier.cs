using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis;

/// <summary>The credential provider was missing or failed, so no connection could be built. It never carries the provider's message.</summary>
internal sealed class RedisCredentialUnavailableException : Exception
{
    public RedisCredentialUnavailableException()
        : base("The coordination credential could not be resolved.")
    {
    }

    public RedisCredentialUnavailableException(string message)
        : base(message)
    {
    }

    public RedisCredentialUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Which token fired when an operation stopped waiting: the caller's or request's, or the adapter's own bound.</summary>
internal enum RedisStop
{
    None,
    Cancelled,
    TimedOut,
}

/// <summary>
/// Contract Failures rule 2 for the Redis adapter (ISK-0005 research R11). A connection failure or a <c>READONLY</c> reply
/// is <c>coordination.unavailable</c>; an authentication reply, or a missing or failing credential provider, is
/// <c>coordination.unavailable</c> with class <c>AuthenticationFailed</c>; a client timeout, <c>operationTimeout</c>, or the
/// deadline is <c>coordination.timeout</c>; the caller's or request's token is <c>coordination.cancelled</c>; anything
/// else is <c>core.internal</c> with class <c>Unexpected</c> and event 5010. A write that may have reached the server and
/// then failed, timed out, or was cancelled has <c>outcome = unknown</c> and event 5004; one stopped before its command was
/// sent, while connecting, during <c>SCRIPT LOAD</c>, or still waiting to be sent, has not. A connection failure or a
/// refused credential writes event 5003; a <c>READONLY</c> reply does not. Nothing is
/// retried, and no server text, key, or host reaches the result (FR-022). Owned by ISK-0005.
/// </summary>
internal static class RedisFailureClassifier
{
    /// <summary>
    /// The contract failure for <paramref name="exception"/>. <paramref name="commandSent"/> says whether the script's own
    /// command was handed to the client, so that a stop before it, such as while connecting, is never an unknown outcome.
    /// </summary>
    public static GatewayException Classify(Exception exception, RedisStop stop, bool commandSent, string module, CoordinationPrimitive primitive, RedisScript script, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(logger);
        var (code, failureClass, sent) = Map(exception, stop, commandSent);
        // A READONLY reply is a write a replica refused over a working connection, not a failed connection attempt.
        if (failureClass is CoordinationFailureClass.Unavailable or CoordinationFailureClass.AuthenticationFailed && !IsReadOnlyReply(exception))
        {
            CoordinationLog.LogConnectionFailed(logger, CoordinatorKind.Redis, failureClass);
        }

        if (failureClass == CoordinationFailureClass.Unexpected)
        {
            CoordinationLog.LogUnexpectedFailure(logger, module, script.Name, CoordinatorKind.Redis, failureClass);
        }

        var outcomeUnknown = script.Writes && sent
            && failureClass is CoordinationFailureClass.Unavailable or CoordinationFailureClass.Timeout or CoordinationFailureClass.Cancelled;
        if (outcomeUnknown)
        {
            CoordinationLog.LogOutcomeUnknown(logger, module, primitive, script.Name, CoordinatorKind.Redis);
        }

        return CoordinationFailures.Raise(code, failureClass, outcomeUnknown);
    }

    /// <summary>
    /// The code, class, and whether the command may have reached the server, for <paramref name="exception"/>: only when
    /// <paramref name="commandSent"/>, and not when the client reports the command still waiting to be sent.
    /// </summary>
    public static (ErrorCode Code, CoordinationFailureClass FailureClass, bool Sent) Map(Exception exception, RedisStop stop, bool commandSent)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var sent = commandSent && exception is not RedisTimeoutException { Commandstatus: CommandStatus.WaitingToBeSent }
            && exception is not RedisConnectionException { CommandStatus: CommandStatus.WaitingToBeSent };
        if (stop == RedisStop.Cancelled)
        {
            return (ErrorCodes.CoordinationCancelled, CoordinationFailureClass.Cancelled, sent);
        }

        if (stop == RedisStop.TimedOut)
        {
            return (ErrorCodes.CoordinationTimeout, CoordinationFailureClass.Timeout, sent);
        }

        return exception switch
        {
            RedisCredentialUnavailableException => (ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.AuthenticationFailed, false),
            RedisConnectionException { FailureType: ConnectionFailureType.AuthenticationFailure } =>
                (ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.AuthenticationFailed, false),
            RedisConnectionException { FailureType: ConnectionFailureType.UnableToConnect or ConnectionFailureType.UnableToResolvePhysicalConnection } =>
                (ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.Unavailable, false),
            RedisConnectionException => (ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.Unavailable, sent),
            RedisTimeoutException => (ErrorCodes.CoordinationTimeout, CoordinationFailureClass.Timeout, sent),
            RedisServerException server when Reply(server, "NOAUTH") || Reply(server, "WRONGPASS") =>
                (ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.AuthenticationFailed, false),
            RedisServerException server when Reply(server, "READONLY") => (ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.Unavailable, false),
            _ => (ErrorCodes.Internal, CoordinationFailureClass.Unexpected, false),
        };
    }

    /// <summary>Whether the failure is an authentication failure, after which the connection is rebuilt with a fresh credential.</summary>
    public static bool IsAuthenticationFailure(Exception exception) =>
        exception is RedisConnectionException { FailureType: ConnectionFailureType.AuthenticationFailure }
        || exception is RedisServerException server && (Reply(server, "NOAUTH") || Reply(server, "WRONGPASS"));

    private static bool IsReadOnlyReply(Exception exception) => exception is RedisServerException server && Reply(server, "READONLY");

    private static bool Reply(RedisServerException exception, string prefix) =>
        exception.Message.StartsWith(prefix, StringComparison.Ordinal);
}
