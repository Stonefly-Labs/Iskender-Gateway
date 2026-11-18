namespace Iskender.Domain.Ports;

/// <summary>
/// A job that runs at most once at a time across every worker, at most once per interval (work contract, Recurring
/// jobs). Every run is safe to repeat after a crash at any point. Owned by ISK-0009.
/// </summary>
public interface IRecurringJob
{
    /// <summary>The job's name, <c>&lt;module&gt;.&lt;name&gt;</c> in <see cref="WorkKind"/>'s shape.</summary>
    string Name { get; }

    /// <summary>Runs one batch of work from <see cref="RecurringJobContext.Cursor"/>, saving the cursor after each page.</summary>
    ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken);
}
