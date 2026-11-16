using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Management.Migrations;

/// <summary>
/// Runs one <c>migrate</c> operation against a container its caller composed with the serving control plane's
/// registrations (container contract, Migration command rules 2 to 6; research R14). It runs the requirement check and
/// validation, then one call to <see cref="IMigrationRunner"/>, and starts nothing: no composition start-up, guard,
/// capability check, coordinator, or listener. It holds no state, so it is static (CA1822), where the data model names a
/// sealed class. Owned by ISK-0007.
/// </summary>
public static class MigrationCommand
{
    private const string PersistencePrefix = "persistence.";

    // Container contract, Command line rule 2. Iskender.Management reaches no infrastructure assembly (rule A3), so these
    // mirror Iskender.Infrastructure.Container's ExitCodes, which MigrationCommandTests compares the outcomes with.
    private const int Success = 0;

    private const int Unexpected = 1;

    private const int ConfigurationRejected = 2;

    private const int RegistrationMissing = 3;

    private const int MigrationFailed = 5;

    private const int SchemaBehind = 6;

    private const int SchemaAhead = 7;

    /// <summary>
    /// Runs <paramref name="operation"/> and returns its outcome: exit code 2 for a rejected configuration and 3 for a
    /// missing registration, each <c>failed</c> with count 0; 0 on success and 5 on any <c>persistence.*</c> failure for
    /// <c>apply</c> and <c>reverse</c>; and for <c>status</c>, 7 when any module is ahead or mismatched, otherwise 6 when
    /// any is behind, otherwise 0, and 5 when the call fails. Any other failure propagates.
    /// </summary>
    public static async ValueTask<MigrationOutcome> RunAsync(IServiceProvider services, MigrationOperation operation, CancellationToken cancellationToken)
    {
        if (services is null || operation is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        try
        {
            await services.GetRequiredService<CompositionLifecycle>().EnsureComposableAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (GatewayException refused) when (refused.Code == ErrorCodes.HostingConfigurationRejected)
        {
            return Failed(operation, ConfigurationRejected, refused.Code);
        }
        catch (GatewayException refused) when (refused.Code == ErrorCodes.HostingRegistrationMissing)
        {
            return Failed(operation, RegistrationMissing, refused.Code);
        }

        var runner = services.GetRequiredService<IMigrationRunner>();
        try
        {
            return operation switch
            {
                MigrationOperation.Apply => Succeeded(operation, (await runner.ApplyAsync(new MigrationApplyRequest(), cancellationToken).ConfigureAwait(false)).Applied.Count),
                MigrationOperation.Reverse reverse => Succeeded(operation, (await runner.ReverseAsync(new MigrationReverseRequest { Steps = reverse.Steps, ConfirmDataLoss = reverse.ConfirmDataLoss }, cancellationToken).ConfigureAwait(false)).Reversed.Count),
                MigrationOperation.Status => Status(operation, await runner.GetStatusAsync(cancellationToken).ConfigureAwait(false)),
                _ => throw new GatewayException(ErrorCodes.ValueInvalid),
            };
        }
        catch (GatewayException failure) when (failure.Code.Id.StartsWith(PersistencePrefix, StringComparison.Ordinal) || operation is MigrationOperation.Status)
        {
            return Failed(operation, MigrationFailed, failure.Code);
        }
    }

    /// <summary>
    /// Runs one <c>migrate</c> command whose arguments have parsed: builds the container with <paramref name="build"/>,
    /// which composes the serving control plane's registrations, runs <paramref name="operation"/> as
    /// <see cref="RunAsync(IServiceProvider, MigrationOperation, CancellationToken)"/> does, and writes event 7006 once,
    /// whatever the outcome (Migration command rule 5). A configuration that <paramref name="build"/> rejects exits 2. Any
    /// other unexpected failure exits 1, <c>failed</c> with count 0, and writes only the exception's type name to
    /// <paramref name="standardError"/>, never its message (Diagnostics rules 4 and 6).
    /// </summary>
    public static async ValueTask<MigrationOutcome> RunAsync(
        Func<IServiceProvider> build,
        MigrationOperation operation,
        ILoggerFactory loggerFactory,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        if (build is null || operation is null || loggerFactory is null || standardError is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        // An unexpected failure keeps this outcome.
        var outcome = Failed(operation, Unexpected, null);
        try
        {
            var services = build();
            try
            {
                outcome = await RunAsync(services, operation, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (services is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (GatewayException rejected) when (rejected.Code == ErrorCodes.HostingConfigurationRejected)
        {
            outcome = Failed(operation, ConfigurationRejected, rejected.Code);
        }
        catch (Exception unexpected)
        {
            await standardError.WriteLineAsync(unexpected.GetType().Name).ConfigureAwait(false);
        }
        finally
        {
            MigrationLog.LogFinished(loggerFactory.CreateLogger(MigrationLog.Category), outcome.Operation, outcome.Outcome, outcome.Count, outcome.Code?.Id);
        }

        return outcome;
    }

    private static MigrationOutcome Succeeded(MigrationOperation operation, int count) => new(Success, operation.Name, "succeeded", count, null);

    private static MigrationOutcome Failed(MigrationOperation operation, int exitCode, ErrorCode? code) => new(exitCode, operation.Name, "failed", 0, code);

    private static MigrationOutcome Status(MigrationOperation operation, IReadOnlyList<MigrationStatus> modules)
    {
        if (modules.Any(module => module.Unknown.Count > 0 || module.ChecksumMismatches.Count > 0 || module.SchemaVersion > module.SupportedRange.Maximum))
        {
            return new(SchemaAhead, operation.Name, "ahead", 0, null);
        }

        if (modules.Any(module => module.Pending.Count > 0 || (module.SchemaVersion ?? 0) < module.SupportedRange.Minimum))
        {
            return new(SchemaBehind, operation.Name, "behind", 0, null);
        }

        return new(Success, operation.Name, "current", 0, null);
    }
}
