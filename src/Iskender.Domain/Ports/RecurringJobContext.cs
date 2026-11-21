using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// One run of a recurring job: its batch size, the cursor the last run saved, and the fenced write that saves a new one
/// (work contract, Recurring jobs rules 1 and 3). The job runner supplies the save. Owned by ISK-0009.
/// </summary>
public sealed class RecurringJobContext
{
    private readonly Func<string?, CancellationToken, ValueTask> _saveCursor;

    /// <summary>A batch below 1, a cursor over 512 characters, or a <c>null</c> save fails with <c>core.value.invalid</c>.</summary>
    public RecurringJobContext(int batch, string? cursor, Func<string?, CancellationToken, ValueTask> saveCursor)
    {
        if (batch < 1 || cursor is { Length: > WorkLimits.MaximumCursorLength } || saveCursor is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Batch = batch;
        Cursor = cursor;
        _saveCursor = saveCursor;
    }

    public int Batch { get; }

    /// <summary>Where this run starts, or <c>null</c> for the beginning. It changes to each saved cursor.</summary>
    public string? Cursor { get; private set; }

    /// <summary>
    /// Saves <paramref name="cursor"/> through the fenced bookkeeping write, so a crash resumes there. A cursor over 512
    /// characters fails with <c>core.value.invalid</c>, and a stale lease with <c>coordination.fencing_rejected</c>.
    /// </summary>
    public async ValueTask SaveCursorAsync(string? cursor, CancellationToken cancellationToken)
    {
        if (cursor is { Length: > WorkLimits.MaximumCursorLength })
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        await _saveCursor(cursor, cancellationToken).ConfigureAwait(false);
        Cursor = cursor;
    }
}
