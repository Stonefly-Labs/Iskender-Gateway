using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Work.CrashHost;

/// <summary>The fixed lines the crash host prints, which the crash tests wait for. They carry no identifier.</summary>
internal static class CrashLines
{
    /// <summary>The point that makes the host a producer.</summary>
    public const string ProducerPoint = "AfterProducerCommit";

    /// <summary>The point that holds nowhere, for the drain.</summary>
    public const string NoPoint = "None";

    /// <summary>Printed once the producer's second units are open and uncommitted.</summary>
    public const string UncommittedLine = "BeforeProducerCommit";

    /// <summary>Printed when the crash-every-attempt handler starts an attempt.</summary>
    public const string CrashLine = "CrashHandler";

    private static readonly Lock Gate = new();

    /// <summary>Prints <c>reached &lt;name&gt;</c> and flushes it.</summary>
    public static void Write(string name)
    {
        lock (Gate)
        {
            Console.Out.WriteLine("reached " + name);
            Console.Out.Flush();
        }
    }
}

/// <summary>Prints each injection point it reaches, and holds forever at the one the command line names.</summary>
internal sealed class CrashInjection(WorkInjectionPoint? held) : IWorkInjection
{
    public async ValueTask ReachedAsync(WorkInjectionPoint point, Guid itemId, CancellationToken cancellationToken)
    {
        CrashLines.Write(point.ToString());
        if (point == held)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}

/// <summary>A handler that never finishes an attempt: it prints its line and waits to be killed (ISK-0009 T034).</summary>
internal sealed class CrashHandler : IWorkHandler
{
    public static WorkKind Kind { get; } = new("applies", "crash");

    public static WorkHandlerRegistration Registration() =>
        new(Kind, 1, 1, maximumAttempts: null, timeout: TimeSpan.FromMinutes(15), hasExternalEffects: false, typeof(CrashHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        CrashLines.Write(CrashLines.CrashLine);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}

/// <summary>The host's credential port: the password from its command line, for every request.</summary>
internal sealed class CrashCredentials(string password) : IDatabaseCredentialProvider
{
    public ValueTask<DatabaseCredential> GetCredentialAsync(DatabaseCredentialRequest request, CancellationToken cancellationToken) =>
        password.Length > 0 ? ValueTask.FromResult(new DatabaseCredential(password)) : throw new GatewayException(ErrorCodes.PersistenceUnavailable);
}
