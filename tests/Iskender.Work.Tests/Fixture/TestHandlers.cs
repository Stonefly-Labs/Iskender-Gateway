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
