using System.Net.Sockets;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Connections;

public class FailureClassifierTests
{
    private const string Planted = "planted-server-text-host=db.secret.internal user=isk_runtime";

    private static PostgresException Server(string sqlState) => new(Planted, "ERROR", "ERROR", sqlState);

    public static TheoryData<string, string, PersistenceFailureClass> SqlStates() => new()
    {
        { "40001", "persistence.concurrency_conflict", PersistenceFailureClass.TransientConflict },
        { "40P01", "persistence.concurrency_conflict", PersistenceFailureClass.TransientConflict },
        { "23505", "persistence.unique_conflict", PersistenceFailureClass.UniqueConflict },
        { "08000", "persistence.unavailable", PersistenceFailureClass.Unavailable },
        { "08006", "persistence.unavailable", PersistenceFailureClass.Unavailable },
        { "57P01", "persistence.unavailable", PersistenceFailureClass.Unavailable },
        { "57P02", "persistence.unavailable", PersistenceFailureClass.Unavailable },
        { "57P03", "persistence.unavailable", PersistenceFailureClass.Unavailable },
        { "28P01", "persistence.unavailable", PersistenceFailureClass.AuthenticationFailed },
        { "28000", "persistence.unavailable", PersistenceFailureClass.AuthenticationFailed },
        { "57014", "persistence.timeout", PersistenceFailureClass.Timeout },
        { "42501", "core.internal", PersistenceFailureClass.Unexpected },
        { "42P01", "core.internal", PersistenceFailureClass.Unexpected },
        { "55P03", "core.internal", PersistenceFailureClass.Unexpected },
    };

    [Theory]
    [MemberData(nameof(SqlStates))]
    public void EachSqlStateMapsToItsClassAndCode(string sqlState, string code, PersistenceFailureClass failureClass)
    {
        var result = FailureClassifier.Classify(Server(sqlState), FailureSite.Statement, FiredToken.None);

        Assert.Equal(code, result.Failure.Code.Id);
        Assert.Equal(failureClass, result.Failure.FailureClass);
        Assert.False(result.Failure.CommitOutcomeUnknown);
        AssertSafe(result.ToException(), sqlState);
    }

    [Fact]
    public void LockNotAvailableDuringAnAdvisoryLockWaitIsALockTimeout()
    {
        var result = FailureClassifier.Classify(Server("55P03"), FailureSite.AdvisoryLockWait, FiredToken.None);

        Assert.Same(ErrorCodes.PersistenceLockTimeout, result.Failure.Code);
        Assert.Equal(PersistenceFailureClass.Timeout, result.Failure.FailureClass);
    }

    [Fact]
    public void QueryCanceledReadsAsTheTokenThatFired()
    {
        Assert.Same(ErrorCodes.PersistenceCancelled, FailureClassifier.Classify(Server("57014"), FailureSite.Statement, FiredToken.Cancellation).Failure.Code);
        Assert.Same(ErrorCodes.PersistenceTimeout, FailureClassifier.Classify(Server("57014"), FailureSite.Statement, FiredToken.Timeout).Failure.Code);
        Assert.Same(ErrorCodes.PersistenceTimeout, FailureClassifier.Classify(Server("57014"), FailureSite.Statement, FiredToken.None).Failure.Code);
    }

    [Fact]
    public void AnOperationCanceledReadsAsTheTokenThatFired()
    {
        var cancelled = new OperationCanceledException(Planted);

        Assert.Equal(PersistenceFailureClass.Cancelled, FailureClassifier.Classify(cancelled, FailureSite.Statement, FiredToken.Cancellation).Failure.FailureClass);
        Assert.Equal(PersistenceFailureClass.Timeout, FailureClassifier.Classify(cancelled, FailureSite.Statement, FiredToken.Timeout).Failure.FailureClass);
        Assert.Equal(PersistenceFailureClass.Timeout, FailureClassifier.Classify(new NpgsqlException(Planted, new TimeoutException(Planted)), FailureSite.Statement, FiredToken.None).Failure.FailureClass);
    }

    [Fact]
    public void SocketIoAndBrokenConnectionsAreUnavailable()
    {
        Exception[] failures =
        [
            new NpgsqlException(Planted, new SocketException((int)SocketError.ConnectionRefused)),
            new NpgsqlException(Planted, new IOException(Planted)),
            new NpgsqlException(Planted),
            new SocketException((int)SocketError.HostNotFound),
        ];

        Assert.All(failures, failure =>
        {
            var result = FailureClassifier.Classify(failure, FailureSite.Statement, FiredToken.None);
            Assert.Same(ErrorCodes.PersistenceUnavailable, result.Failure.Code);
            Assert.Equal(PersistenceFailureClass.Unavailable, result.Failure.FailureClass);
            AssertSafe(result.ToException(), sqlState: null);
        });
    }

    [Fact]
    public void AMissingOrFailingCredentialProviderIsAuthenticationFailed()
    {
        var result = FailureClassifier.Classify(new NpgsqlException(Planted, new CredentialUnavailableException()), FailureSite.Statement, FiredToken.None);

        Assert.Same(ErrorCodes.PersistenceUnavailable, result.Failure.Code);
        Assert.Equal(PersistenceFailureClass.AuthenticationFailed, result.Failure.FailureClass);
    }

    [Fact]
    public void ABrokenConnectionDuringCommitHasAnUnknownOutcome()
    {
        var result = FailureClassifier.Classify(new NpgsqlException(Planted, new IOException(Planted)), FailureSite.Commit, FiredToken.None);

        Assert.Same(ErrorCodes.PersistenceUnavailable, result.Failure.Code);
        Assert.True(result.Failure.CommitOutcomeUnknown);
        Assert.True(FailureClassifier.Classify(new OperationCanceledException(), FailureSite.Commit, FiredToken.Cancellation).Failure.CommitOutcomeUnknown);
    }

    [Fact]
    public void AServerErrorDuringCommitHasAKnownOutcome()
    {
        var result = FailureClassifier.Classify(Server("40001"), FailureSite.Commit, FiredToken.None);

        Assert.Equal(PersistenceFailureClass.TransientConflict, result.Failure.FailureClass);
        Assert.False(result.Failure.CommitOutcomeUnknown);
    }

    [Fact]
    public void AnythingElseIsUnexpectedWithOnlyTheSqlStateClass()
    {
        Assert.Equal("42", FailureClassifier.Classify(Server("42501"), FailureSite.Statement, FiredToken.None).SqlStateClass);
        var other = FailureClassifier.Classify(new InvalidOperationException(Planted), FailureSite.Statement, FiredToken.None);
        Assert.Same(ErrorCodes.Internal, other.Failure.Code);
        Assert.Equal(PersistenceFailureClass.Unexpected, other.Failure.FailureClass);
        Assert.Null(other.SqlStateClass);
    }

    [Fact]
    public void AnInnerServerErrorIsFoundThroughWrappers()
    {
        var wrapped = new InvalidOperationException("outer", new AggregateException(Server("23505")));

        Assert.Equal(PersistenceFailureClass.UniqueConflict, FailureClassifier.Classify(wrapped, FailureSite.Statement, FiredToken.None).Failure.FailureClass);
    }

    private static void AssertSafe(GatewayException exception, string? sqlState)
    {
        Assert.Null(exception.InnerException);
        Assert.Equal($"{exception.Code.Id}: {exception.Code.SafeMessage}", exception.Message);
        Assert.DoesNotContain(Planted, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("db.secret.internal", exception.ToString(), StringComparison.Ordinal);
        if (sqlState is not null)
        {
            Assert.DoesNotContain(sqlState, exception.Message, StringComparison.Ordinal);
        }
    }
}
