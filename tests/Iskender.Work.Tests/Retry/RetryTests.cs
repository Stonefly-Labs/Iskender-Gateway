using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Retry;

/// <summary>
/// Retry and poison (ISK-0009 T033, US3.1, US3.2, US3.5, SC-004, FR-016, FR-017): a transient failure is due again within
/// its attempt's backoff, a permanent one poisons at once, an unexpected exception poisons after exactly the maximum,
/// a registration's maximum replaces the setting, and the events and metrics carry the class and code.
/// </summary>
public sealed class RetryTests(WorkFixture work) : IClassFixture<WorkFixture>, IDisposable
{
    private const string Settings = "\"pollInterval\":\"1s\",\"leaseDuration\":\"5s\",\"retry\":{\"initialDelay\":\"1s\",\"maximumDelay\":\"4s\",\"maximumAttempts\":4}";

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly MeterListener _meters = Listen();

    private static ConcurrentQueue<(string Instrument, string? Kind, string? Class, string? Code)> Measurements { get; } = new();

    public void Dispose() => _meters.Dispose();

    [Fact]
    public async Task ATransientFailureIsDueWithinEachAttemptsBackoffUntilItIsPoisonedAtTheMaximum()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TransientHandler.Kind, "transient");
        var before = work.Logger.Events.Count;

        await using var dispatcher = new TestDispatcher(work, [TransientHandler.Registration()], Settings);
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var sent = await NowAsync();
            await dispatcher.PassAsync();
            var received = await NowAsync();

            if (attempt < 4)
            {
                Assert.Equal($"pending|{attempt}", await work.ItemAsync(item));
                var ceiling = TimeSpan.FromTicks(Math.Min(InitialDelay.Ticks << (attempt - 1), MaximumDelay.Ticks));
                var floor = TimeSpan.FromTicks(Math.Max(InitialDelay.Ticks, ceiling.Ticks / 2));
                var due = await work.ScalarAsync<DateTime>($"SELECT due_at FROM isk_work.items WHERE item_id = '{item}'");
                Assert.InRange(due, sent + floor, received + ceiling);
                Assert.True(ceiling <= MaximumDelay);

                // Not yet due: a pass at once dispatches nothing, and the next poll after the due time does.
                await dispatcher.PassAsync();
                Assert.Equal($"pending|{attempt}", await work.ItemAsync(item));
                await work.MakeDueAsync(item);
            }
        }

        Assert.Equal("poisoned|4", await work.ItemAsync(item));
        Assert.Equal("transient|persistence.timeout", await FailureAsync(item));
        var events = work.Logger.Events.Skip(before).ToList();
        Assert.Equal(3, events.Count(logged => logged.Id.Id == 8006 && Carries(logged, "transient", "persistence.timeout")));
        Assert.Single(events, logged => logged.Id.Id == 8007 && Carries(logged, "transient", "persistence.timeout"));
        Assert.Equal(3, Counted("iskender.work.items.retried", TransientHandler.Kind, "transient", "persistence.timeout"));
        Assert.Equal(1, Counted("iskender.work.items.poisoned", TransientHandler.Kind, "transient", "persistence.timeout"));
    }

    [Fact]
    public async Task TheNextPollDispatchesARetryNoLaterThanTheMaximumDelayPlusOnePollInterval()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TransientHandler.Kind, "polled");
        await using var dispatcher = new TestDispatcher(work, [TransientHandler.Registration()], Settings);
        await dispatcher.PassAsync();
        var failed = TimeProvider.System.GetTimestamp();

        dispatcher.Start();
        await Eventually.HoldsAsync(async () => await work.ItemAsync(item) != "pending|1", "the retry");

        Assert.InRange(TimeProvider.System.GetElapsedTime(failed), TimeSpan.Zero, MaximumDelay + PollInterval + TimeSpan.FromSeconds(1));
    }

    public static TheoryData<bool> PermanentCodes() => [true, false];

    [Theory]
    [MemberData(nameof(PermanentCodes))]
    public async Task APermanentFailurePoisonsOnTheFirstAttemptWithItsClassAndCode(bool payloadInvalid)
    {
        work.RequirePostgreSql();
        var registration = payloadInvalid ? PayloadInvalidHandler.Registration() : DeclaredPermanentHandler.Registration();
        var code = payloadInvalid ? ErrorCodes.WorkPayloadInvalid : ErrorCodes.ValueInvalid;
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), registration.Kind, "permanent");
        var before = work.Logger.Events.Count;

        await using (var dispatcher = new TestDispatcher(work, [registration], Settings))
        {
            await dispatcher.PassAsync();
        }

        Assert.Equal("poisoned|1", await work.ItemAsync(item));
        Assert.Equal("permanent|" + code.Id, await FailureAsync(item));
        Assert.Single(work.Logger.Events.Skip(before), logged => logged.Id.Id == 8007 && Carries(logged, "permanent", code.Id));
        Assert.Equal(1, Counted("iskender.work.items.poisoned", registration.Kind, "permanent", code.Id));
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData(2, 2)]
    public async Task AnUnexpectedExceptionRetriesAndIsPoisonedAfterExactlyItsMaximum(int? registered, int maximum)
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), UnexpectedHandler.Kind, "unexpected-" + maximum);

        await using (var dispatcher = new TestDispatcher(work, [UnexpectedHandler.Registration(registered)], Settings))
        {
            for (var attempt = 1; attempt <= maximum; attempt++)
            {
                Assert.Equal($"pending|{attempt - 1}", await work.ItemAsync(item));
                await dispatcher.PassAsync();
                await work.MakeDueAsync(item);
            }

            await dispatcher.PassAsync();
        }

        Assert.Equal($"poisoned|{maximum}", await work.ItemAsync(item));
        Assert.Equal("unclassified|core.internal", await FailureAsync(item));
    }

    private static bool Carries(CapturedLog logged, string failureClass, string code) =>
        logged.Fields.Any(field => field.Key == "FailureClass" && field.Value?.ToString() == failureClass)
        && logged.Fields.Any(field => field.Key == "Code" && field.Value?.ToString() == code);

    private static int Counted(string instrument, WorkKind kind, string failureClass, string code) =>
        Measurements.Count(measurement => measurement == (instrument, kind.ToString(), failureClass, code));

    private static MeterListener Listen()
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, listening) =>
            {
                if (instrument.Meter.Name == WorkTelemetry.SourceName)
                {
                    listening.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, labels, _) =>
        {
            var values = labels.ToArray();
            string? Label(string name) => values.FirstOrDefault(label => label.Key == name).Value?.ToString();
            Measurements.Enqueue((instrument.Name, Label(WorkTelemetry.KindLabel), Label(WorkTelemetry.FailureClassLabel), Label(WorkTelemetry.ErrorCodeLabel)));
        });
        listener.Start();
        return listener;
    }

    private Task<DateTime> NowAsync() => work.ScalarAsync<DateTime>("SELECT clock_timestamp()");

    private Task<string> FailureAsync(Guid item) =>
        work.ScalarAsync<string>($"SELECT last_failure_class || '|' || last_failure_code FROM isk_work.items WHERE item_id = '{item}'");

    private sealed class TransientHandler : IWorkHandler
    {
        public static WorkKind Kind { get; } = new("applies", "transient");

        public static WorkHandlerRegistration Registration() => new(Kind, 1, 1, null, null, false, typeof(TransientHandler));

        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) =>
            throw new GatewayException(ErrorCodes.PersistenceTimeout);
    }

    private sealed class PayloadInvalidHandler : IWorkHandler
    {
        public static WorkKind Kind { get; } = new("applies", "payload_invalid");

        public static WorkHandlerRegistration Registration() => new(Kind, 1, 1, null, null, false, typeof(PayloadInvalidHandler));

        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) =>
            throw WorkFailures.Permanent(ErrorCodes.WorkPayloadInvalid);
    }

    private sealed class DeclaredPermanentHandler : IWorkHandler
    {
        public static WorkKind Kind { get; } = new("applies", "declared_permanent");

        public static WorkHandlerRegistration Registration() => new(Kind, 1, 1, null, null, false, typeof(DeclaredPermanentHandler));

        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) =>
            throw WorkFailures.Permanent(ErrorCodes.ValueInvalid);
    }

    private sealed class UnexpectedHandler : IWorkHandler
    {
        public static WorkKind Kind { get; } = new("applies", "unexpected");

        public static WorkHandlerRegistration Registration(int? maximumAttempts) => new(Kind, 1, 1, maximumAttempts, null, false, typeof(UnexpectedHandler));

        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("unexpected");
    }
}
