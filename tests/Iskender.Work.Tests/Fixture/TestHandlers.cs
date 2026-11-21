using System.Collections.Concurrent;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;

namespace Iskender.Work.Tests.Fixture;

/// <summary>The kinds the harness's handlers serve, in the fixture-only module <c>applies</c>.</summary>
internal static class TestKinds
{
    public static WorkKind Record { get; } = new("applies", "record");

    public static WorkKind FailOnce { get; } = new("applies", "fail_once");
}

/// <summary>Writes one <c>applies</c> row per apply, through the handler's unit (ISK-0009 T022).</summary>
internal sealed class ApplyRecordingHandler : IWorkHandler
{
    /// <summary>Every delivery this process handed to a handler of this type, in order.</summary>
    public static ConcurrentQueue<WorkDelivery> Deliveries { get; } = new();

    public static WorkHandlerRegistration Registration(WorkKind? kind = null, int? maximumAttempts = null, TimeSpan? timeout = null) =>
        new(kind ?? TestKinds.Record, 1, 1, maximumAttempts, timeout, hasExternalEffects: false, typeof(ApplyRecordingHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        Deliveries.Enqueue(delivery);
        await RecordAsync(delivery, unit, cancellationToken);
    }

    public static async ValueTask RecordAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) =>
        await PostgreSqlUnitOfWork.From(unit).Command(AppliesModule.Applies, "INSERT INTO isk_applies.applies (scope_key, item_id, attempt) VALUES ($1, $2, $3)")
            .With(delivery.ItemId).With(delivery.Attempt).ExecuteAsync(cancellationToken);
}

/// <summary>
/// Fails its first attempt with <c>persistence.timeout</c>, a transient failure, then applies like
/// <see cref="ApplyRecordingHandler"/>, so a test reaches the failure record's injection points (ISK-0009 T022).
/// </summary>
internal sealed class FailOnceHandler : IWorkHandler
{
    public static WorkHandlerRegistration Registration(int? maximumAttempts = null) =>
        new(TestKinds.FailOnce, 1, 1, maximumAttempts, timeout: null, hasExternalEffects: false, typeof(FailOnceHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        if (delivery.Attempt == 1)
        {
            throw new GatewayException(ErrorCodes.PersistenceTimeout);
        }

        await ApplyRecordingHandler.RecordAsync(delivery, unit, cancellationToken);
    }
}

/// <summary>
/// Samples its own dispatcher's in-flight count, one instance per dispatcher, holding each delivery briefly so items
/// overlap, then applies like <see cref="ApplyRecordingHandler"/> (ISK-0009 T028).
/// </summary>
internal sealed class SamplingHandler : IWorkHandler
{
    private int _inFlight;
    private int _maximum;
    private int _handled;

    public static WorkKind Kind { get; } = new("applies", "sample");

    /// <summary>The most deliveries this instance ran at once.</summary>
    public int Maximum => Volatile.Read(ref _maximum);

    /// <summary>The deliveries this instance applied.</summary>
    public int Handled => Volatile.Read(ref _handled);

    public static WorkHandlerRegistration Registration() =>
        new(Kind, 1, 1, maximumAttempts: null, timeout: null, hasExternalEffects: false, typeof(SamplingHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        var now = Interlocked.Increment(ref _inFlight);
        try
        {
            int seen;
            while (now > (seen = Volatile.Read(ref _maximum)) && Interlocked.CompareExchange(ref _maximum, now, seen) != seen)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            await ApplyRecordingHandler.RecordAsync(delivery, unit, cancellationToken);
            Interlocked.Increment(ref _handled);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

/// <summary>
/// Writes its <c>applies</c> row, then hangs until its token is cancelled, recording how long it ran (ISK-0009 T030).
/// </summary>
internal sealed class HangHandler : IWorkHandler
{
    public static WorkKind Kind { get; } = new("applies", "hang");

    /// <summary>Each item's time from delivery to cancellation.</summary>
    public static ConcurrentDictionary<Guid, TimeSpan> Ran { get; } = new();

    public static WorkHandlerRegistration Registration(TimeSpan? timeout = null, int? maximumAttempts = null) =>
        new(Kind, 1, 1, maximumAttempts, timeout, hasExternalEffects: false, typeof(HangHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        await ApplyRecordingHandler.RecordAsync(delivery, unit, cancellationToken);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            Ran[delivery.ItemId] = TimeProvider.System.GetElapsedTime(started);
        }
    }
}

/// <summary>
/// Runs for <see cref="Duration"/>, about two thirds of its <see cref="Timeout"/>, then applies, recording each delivery's
/// attempt number (ISK-0009 T079).
/// </summary>
internal sealed class PacedHandler : IWorkHandler
{
    public static WorkKind Kind { get; } = new("applies", "paced");

    public static TimeSpan Duration { get; } = TimeSpan.FromMilliseconds(1300);

    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(2);

    public static WorkHandlerRegistration Registration() =>
        new(Kind, 1, 1, maximumAttempts: null, Timeout, hasExternalEffects: false, typeof(PacedHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        await Task.Delay(Duration, cancellationToken);
        await ApplyRecordingHandler.RecordAsync(delivery, unit, cancellationToken);
    }
}

/// <summary>Runs for <see cref="Duration"/>, longer than a 5 s lease, then applies (ISK-0009 T030).</summary>
internal sealed class SlowHandler : IWorkHandler
{
    public static WorkKind Kind { get; } = new("applies", "slow");

    public static TimeSpan Duration { get; } = TimeSpan.FromSeconds(7);

    public static WorkHandlerRegistration Registration() =>
        new(Kind, 1, 1, maximumAttempts: null, TimeSpan.FromSeconds(30), hasExternalEffects: false, typeof(SlowHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        await Task.Delay(Duration, cancellationToken);
        await ApplyRecordingHandler.RecordAsync(delivery, unit, cancellationToken);
    }
}

/// <summary>
/// Runs for <see cref="Duration"/>, past the 60 s default bound of a unit, within its 90 s timeout, then applies (ISK-0009
/// T065).
/// </summary>
internal sealed class LongHandler : IWorkHandler
{
    public static WorkKind Kind { get; } = new("applies", "long");

    public static TimeSpan Duration { get; } = TimeSpan.FromSeconds(62);

    public static WorkHandlerRegistration Registration() =>
        new(Kind, 1, 1, maximumAttempts: null, TimeSpan.FromSeconds(90), hasExternalEffects: false, typeof(LongHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        await Task.Delay(Duration, cancellationToken);
        await ApplyRecordingHandler.RecordAsync(delivery, unit, cancellationToken);
    }
}

/// <summary>
/// Writes its <c>applies</c> row, then hangs inside a statement of its unit until its token cancels the statement, so it
/// fails with <c>persistence.cancelled</c> rather than an <see cref="OperationCanceledException"/> (ISK-0009 T066).
/// </summary>
internal sealed class StatementHangHandler : IWorkHandler
{
    public static WorkKind Kind { get; } = new("applies", "statement_hang");

    /// <summary>The exception each item's handler ended with.</summary>
    public static ConcurrentDictionary<Guid, Exception> Failed { get; } = new();

    public static WorkHandlerRegistration Registration(TimeSpan? timeout = null) =>
        new(Kind, 1, 1, maximumAttempts: null, timeout, hasExternalEffects: false, typeof(StatementHangHandler));

    public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        await ApplyRecordingHandler.RecordAsync(delivery, unit, cancellationToken);
        try
        {
            await PostgreSqlUnitOfWork.From(unit).Command(AppliesModule.Applies, "SELECT count(*) FROM isk_applies.applies, pg_sleep(600) WHERE {scope}")
                .QueryAsync(reader => reader.GetInt64(0), cancellationToken);
        }
        catch (Exception exception)
        {
            Failed[delivery.ItemId] = exception;
            throw;
        }
    }
}
