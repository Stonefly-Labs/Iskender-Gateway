using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Management.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Management.Bootstrap;

/// <summary>
/// Runs one <c>bootstrap</c> operation against a container its caller composed with the serving control plane's
/// registrations (container contract, Bootstrap command; research R6 and R14). It runs the requirement check and
/// validation, then one call to <see cref="IOperatorTokens.IssueAsync"/> with actor <c>operator_command</c>, and starts
/// nothing: no composition start-up, guard, capability check, coordinator, or listener. Only after that call returns,
/// once the token's row is committed, does it write the token and a newline to standard output, and it writes nothing
/// else there; a run that does not exit 0 prints no token. It holds no state, so it is static. Owned by ISK-0010.
/// </summary>
public static class BootstrapCommand
{
    private const string PersistencePrefix = "persistence.";

    private const string ConfigurationPrefix = "config.";

    // Container contract, Command line rule 2 and Bootstrap command rule 5. Iskender.Management reaches no infrastructure
    // assembly (rule A3), so these mirror Iskender.Infrastructure.Container's ExitCodes.
    private const int Success = 0;

    private const int Unexpected = 1;

    private const int ConfigurationRejected = 2;

    private const int RegistrationMissing = 3;

    private const int PersistenceFailed = 5;

    private const int AlreadyInitialized = 8;

    private const int NotInitialized = 9;

    /// <summary>
    /// Builds the container with <paramref name="buildServices"/> and runs <paramref name="operation"/>: exit code 0 when
    /// the token is issued, 2 for a rejected configuration, 3 for a missing registration, 5 for a <c>persistence.*</c>
    /// failure, 8 for <c>issue</c> on an initialized installation, and 9 for <c>recover</c> on an uninitialized one. Any
    /// other failure exits 1 and writes only the exception's type name to <paramref name="standardError"/>, never its
    /// message. Writes event 9004 and the token metrics for an issued token. A <c>null</c> argument or an undefined
    /// operation fails with <c>core.value.invalid</c>.
    /// </summary>
    public static async ValueTask<BootstrapOutcome> RunAsync(
        Func<ServiceProvider> buildServices,
        BootstrapOperation operation,
        ILoggerFactory loggerFactory,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        if (buildServices is null || !Enum.IsDefined(operation) || loggerFactory is null || standardOutput is null || standardError is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var purpose = operation.ToPurpose();
        try
        {
            await using var services = buildServices();
            try
            {
                await services.GetRequiredService<CompositionLifecycle>().EnsureComposableAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (GatewayException refused) when (refused.Code == ErrorCodes.HostingRegistrationMissing)
            {
                return new(RegistrationMissing, refused.Code);
            }

            var context = new OperatorRequestContext(
                RequestCorrelationId.New(), services.GetRequiredService<IValidatedConfiguration>().Snapshot.Revision, OperatorActor.OperatorCommand);
            IssuedOperatorToken issued;
            try
            {
                issued = await services.GetRequiredService<IOperatorTokens>().IssueAsync(purpose, context, cancellationToken).ConfigureAwait(false);
            }
            catch (GatewayException refused) when (Refusal(refused.Code) is { } exitCode)
            {
                return new(exitCode, refused.Code);
            }

            new OperatorLog(loggerFactory, timeProvider: null).TokenIssued(purpose, issued.Superseded);
            services.GetService<OperatorTelemetry>()?.RecordIssued(purpose, issued.Superseded);
            await standardOutput.WriteAsync(issued.Reveal() + "\n").ConfigureAwait(false);
            await standardOutput.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new(Success, null);
        }
        catch (GatewayException rejected) when (rejected.Code == ErrorCodes.HostingConfigurationRejected || rejected.Code.Id.StartsWith(ConfigurationPrefix, StringComparison.Ordinal))
        {
            // As for serving: a configuration a module rejects when its settings are first resolved, such as ISK-0004's
            // poolMinimum above poolMaximum, exits 2 too (Bootstrap command rule 5; CONV-0010-008).
            return new(ConfigurationRejected, rejected.Code);
        }
        catch (Exception unexpected) when (unexpected is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await standardError.WriteLineAsync(unexpected.GetType().Name).ConfigureAwait(false);
            return new(Unexpected, null);
        }
    }

    private static int? Refusal(ErrorCode code) =>
        code == ErrorCodes.OperatorAlreadyInitialized ? AlreadyInitialized
        : code == ErrorCodes.OperatorNotInitialized ? NotInitialized
        : code.Id.StartsWith(PersistencePrefix, StringComparison.Ordinal) ? PersistenceFailed
        : null;
}
