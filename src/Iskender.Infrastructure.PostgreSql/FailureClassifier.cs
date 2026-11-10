using System.Net.Sockets;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>Where a failure happened, which changes how some failures read (research R8).</summary>
internal enum FailureSite
{
    Statement,
    AdvisoryLockWait,
    Commit,
}

/// <summary>Which of a unit's tokens fired, if any. The classifier decides timeout or cancelled from this, not from the SQLSTATE alone.</summary>
internal enum FiredToken
{
    None,
    Timeout,
    Cancellation,
}

/// <summary>A classified failure: what the unit records, and the SQLSTATE class for event 4014 when it is unexpected.</summary>
internal sealed record Classification(PersistenceFailure Failure, string? SqlStateClass)
{
    /// <summary>The exception a port throws: the code alone, with no inner exception, SQL, or server text (research R9).</summary>
    public GatewayException ToException() => new(Failure.Code);
}

/// <summary>
/// The contract's Failures classification table, applied once for every module (FR-013, research R8). Raw driver
/// exceptions never cross a port. Owned by ISK-0004.
/// </summary>
internal static class FailureClassifier
{
    public static Classification Classify(Exception exception, FailureSite site, FiredToken fired)
    {
        var (code, failureClass, sqlStateClass) = Map(exception, site, fired);
        var outcomeUnknown = site == FailureSite.Commit && Find<PostgresException>(exception) is null
            && failureClass is PersistenceFailureClass.Unavailable or PersistenceFailureClass.Timeout or PersistenceFailureClass.Cancelled;
        if (outcomeUnknown)
        {
            (code, failureClass) = (ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.Unavailable);
        }

        return new Classification(new PersistenceFailure(code, failureClass, outcomeUnknown), sqlStateClass);
    }

    /// <summary>A failure the adapter raises itself, before any I/O, such as a scope violation.</summary>
    public static Classification Raised(ErrorCode code, PersistenceFailureClass failureClass) => new(new PersistenceFailure(code, failureClass), SqlStateClass: null);

    private static (ErrorCode Code, PersistenceFailureClass Class, string? SqlStateClass) Map(Exception exception, FailureSite site, FiredToken fired)
    {
        if (Find<CredentialUnavailableException>(exception) is not null)
        {
            return (ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.AuthenticationFailed, null);
        }

        var postgres = Find<PostgresException>(exception);
        var sqlState = postgres?.SqlState;
        // A cancelled statement (57014 from a cancel request or statement_timeout), a client-side cancel or timeout, or any
        // client failure after one of the unit's tokens fired, reads as whichever token fired.
        var timedOut = sqlState == "57014" || (postgres is null && Find<TimeoutException>(exception) is not null);
        var stopped = timedOut || (postgres is null && (Find<OperationCanceledException>(exception) is not null || fired != FiredToken.None));
        if (stopped)
        {
            return fired == FiredToken.Cancellation || (fired == FiredToken.None && !timedOut)
                ? (ErrorCodes.PersistenceCancelled, PersistenceFailureClass.Cancelled, null)
                : (ErrorCodes.PersistenceTimeout, PersistenceFailureClass.Timeout, null);
        }

        if (sqlState is not null)
        {
            return sqlState switch
            {
                "40001" or "40P01" => (ErrorCodes.PersistenceConcurrencyConflict, PersistenceFailureClass.TransientConflict, null),
                "23505" => (ErrorCodes.PersistenceUniqueConflict, PersistenceFailureClass.UniqueConflict, null),
                "55P03" when site == FailureSite.AdvisoryLockWait => (ErrorCodes.PersistenceLockTimeout, PersistenceFailureClass.Timeout, null),
                "28P01" or "28000" => (ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.AuthenticationFailed, null),
                "57P01" or "57P02" or "57P03" => (ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.Unavailable, null),
                _ when sqlState.StartsWith("08", StringComparison.Ordinal) => (ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.Unavailable, null),
                _ => (ErrorCodes.Internal, PersistenceFailureClass.Unexpected, SqlStateClassOf(sqlState)),
            };
        }

        if (Find<NpgsqlException>(exception) is not null || Find<SocketException>(exception) is not null || Find<IOException>(exception) is not null)
        {
            return (ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.Unavailable, null);
        }

        return (ErrorCodes.Internal, PersistenceFailureClass.Unexpected, null);
    }

    /// <summary>The two-character SQLSTATE class, the only part of a server error event 4014 may carry.</summary>
    private static string? SqlStateClassOf(string sqlState) =>
        sqlState.Length == 5 && sqlState.All(char.IsAsciiLetterOrDigit) ? sqlState[..2] : null;

    private static T? Find<T>(Exception? exception)
        where T : Exception
    {
        for (var depth = 0; exception is not null && depth < 16; depth++, exception = exception.InnerException)
        {
            if (exception is T match)
            {
                return match;
            }

            if (exception is AggregateException { InnerExceptions.Count: > 0 } aggregate)
            {
                exception = aggregate.InnerExceptions[0];
                if (exception is T inner)
                {
                    return inner;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Thrown from the password callback when no credential provider is registered or the provider failed. It carries no
/// text from the provider, whose exception is dropped (research R17).
/// </summary>
internal sealed class CredentialUnavailableException : Exception
{
    public CredentialUnavailableException()
        : base("The database credential is unavailable.")
    {
    }

    public CredentialUnavailableException(string message)
        : base(message)
    {
    }

    public CredentialUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
