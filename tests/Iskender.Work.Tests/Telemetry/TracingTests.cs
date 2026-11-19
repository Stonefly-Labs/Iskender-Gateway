using System.Collections.Concurrent;
using System.Diagnostics;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Telemetry;

/// <summary>
/// Tracing (ISK-0009 T052, research R15, <c>work</c> contract Tracing, US7.1, FR-032): enqueue records the caller's
/// <c>traceparent</c>, and its <c>tracestate</c> only up to 512 characters; a dispatch is a <c>Consumer</c> activity with
/// no parent and one link to the recorded context, or none when it does not parse; the dispatch and job activities carry
/// only their contract's tags; and a dispatch discarded for a lost lease ends as <c>lease_lost</c>. The class's kind is
/// its own, so other classes' dispatches never match.
/// </summary>
public sealed class TracingTests : IClassFixture<WorkFixture>, IDisposable
{
    private static readonly WorkKind Traced = new("applies", "traced");

    // Matched by name: comparing the source itself would create it inside ShouldListenTo, while the listener registers.
    private const string CallerName = "Iskender.Work.Tests.Caller";

    private static readonly ActivitySource Caller = new(CallerName);

    private readonly WorkFixture _work;
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public TracingTests(WorkFixture work)
    {
        _work = work;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is WorkTelemetry.SourceName or CallerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDispatchLinksToTheEnqueuersTraceAndIsNotItsChild()
    {
        _work.RequirePostgreSql();
        ActivityContext enqueuer;
        Guid item;
        using (var caller = Caller.StartActivity("enqueue", ActivityKind.Server)!)
        {
            caller.TraceStateString = "vendor=value";
            enqueuer = caller.Context;
            item = await _work.EnqueueAsync(WorkFixture.FreshOrganization(), Traced, "linked");
            Assert.Equal(caller.Id, await StoredAsync(item, "trace_parent"));
        }

        Assert.Equal("vendor=value", await StoredAsync(item, "trace_state"));
        var dispatch = await DispatchAsync(item);

        Assert.Equal(ActivityKind.Consumer, dispatch.Kind);
        Assert.Equal(default, dispatch.ParentSpanId);
        Assert.NotEqual(enqueuer.TraceId, dispatch.TraceId);
        var link = Assert.Single(dispatch.Links);
        Assert.Equal((enqueuer.TraceId, enqueuer.SpanId, "vendor=value", true), (link.Context.TraceId, link.Context.SpanId, link.Context.TraceState, link.Context.IsRemote));
        Assert.Equal(["iskender.work.attempt", "iskender.work.kind", "iskender.work.outcome"], dispatch.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal));
        Assert.Equal(("applies.traced", 1, "completed"), (dispatch.GetTagItem("iskender.work.kind"), dispatch.GetTagItem("iskender.work.attempt"), dispatch.GetTagItem("iskender.work.outcome")));
    }

    [Fact]
    public async Task AnOverLongTracestateIsNotStored()
    {
        _work.RequirePostgreSql();
        Guid item;
        using (var caller = Caller.StartActivity("enqueue")!)
        {
            caller.TraceStateString = "vendor=" + new string('a', 506);
            item = await _work.EnqueueAsync(WorkFixture.FreshOrganization(), Traced, "long-state");
            Assert.Equal(caller.Id, await StoredAsync(item, "trace_parent"));
        }

        Assert.Equal("null", await StoredAsync(item, "trace_state"));
        await DispatchAsync(item);
    }

    [Theory]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-0000000000000000-01")]
    public async Task AStoredContextThatDoesNotParseAddsNoLink(string stored)
    {
        _work.RequirePostgreSql();
        var item = await _work.EnqueueAsync(WorkFixture.FreshOrganization(), Traced, "malformed-" + stored.Length);
        await _work.ExecuteAsync($"UPDATE isk_work.items SET trace_parent = '{stored}', trace_state = NULL WHERE item_id = '{item}'");

        var dispatch = await DispatchAsync(item);

        Assert.Empty(dispatch.Links);
        Assert.Equal(default, dispatch.ParentSpanId);
    }

    // A lease lost after the apply unit opens is discarded at its accept as lease_lost, and the dispatch span says the same
    // as event 8010 (ISK-0009 T072, Tracing rule 2).
    [Fact]
    public async Task AnApplyDiscardedForALostLeaseEndsItsSpanAsLeaseLost()
    {
        _work.RequirePostgreSql();
        var item = await _work.EnqueueAsync(WorkFixture.FreshOrganization(), Traced, "lease-lost");
        var before = _work.Logger.Events.Count;

        await using (var dispatcher = new TestDispatcher(_work, [ApplyRecordingHandler.Registration(Traced)], WorkTestSettings.Fast))
        {
            // Once the apply unit is open, the lease row vanishes, and the unit is handed over only after the next renewal
            // has failed and cancelled the handler's token, which the lost lease is linked to.
            dispatcher.Units.AfterOpen = async (operation, token) =>
            {
                if (operation != "work.apply")
                {
                    return;
                }

                await _work.ExecuteAsync("DELETE FROM isk_coordination.leases");
                await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { }, TaskScheduler.Default);
            };
            await dispatcher.PassAsync();
        }

        Assert.Equal("pending|1", await _work.ItemAsync(item));
        Assert.Contains(_work.Logger.Events.Skip(before), logged => logged.Id.Id == 8010 && logged.Fields.Any(field => field.Key == "Outcome" && (string?)field.Value == WorkOutcomes.LeaseLost));
        var dispatch = _stopped.Single(activity => activity.OperationName == "work.dispatch" && (string?)activity.GetTagItem("iskender.work.kind") == "applies.traced");
        Assert.Equal(WorkOutcomes.LeaseLost, dispatch.GetTagItem("iskender.work.outcome"));
        await _work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{item}'");
    }

    [Fact]
    public async Task AJobRunCarriesOnlyItsJobAndOutcome()
    {
        _work.RequirePostgreSql();
        await using var dispatcher = new TestDispatcher(_work, [], WorkTestSettings.Fast);
        var name = $"applies.traced_{Guid.NewGuid():N}"[..40];
        var interval = TimeSpan.FromMinutes(1);
        var scheduled = new ScheduledJob(new RecurringJobRegistration(name, interval, 1, typeof(CompletingJob)), interval, 1, dispatcher.Leases, new PostgreSqlFencingGuard(_work.Logger));
        var runner = new JobRunner(dispatcher.Units, [scheduled], _ => new CompletingJob(name), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), _work.Logger);

        Assert.Equal("completed", await runner.CheckAsync(scheduled, Token));

        var job = _stopped.Single(activity => activity.OperationName == "work.job" && (string?)activity.GetTagItem("iskender.work.job") == name);
        Assert.Equal(["iskender.work.job", "iskender.work.outcome"], job.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal));
        Assert.Equal("completed", job.GetTagItem("iskender.work.outcome"));
        Assert.Empty(job.Links);
    }

    // A start that finds the job no longer due, because another worker started it after the check, is not a run, so it
    // exports no work.job span (ISK-0009 T091, Tracing rule 3).
    [Fact]
    public async Task AStartThatFindsTheJobNotDueExportsNoJobSpan()
    {
        _work.RequirePostgreSql();
        await using var dispatcher = new TestDispatcher(_work, [], WorkTestSettings.Fast);
        var name = $"applies.notdue_{Guid.NewGuid():N}"[..40];
        var interval = TimeSpan.FromMinutes(1);
        var scheduled = new ScheduledJob(new RecurringJobRegistration(name, interval, 1, typeof(CompletingJob)), interval, 1, dispatcher.Leases, new PostgreSqlFencingGuard(_work.Logger));
        var runner = new JobRunner(dispatcher.Units, [scheduled], _ => new CompletingJob(name), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), _work.Logger);
        Assert.Equal("completed", await runner.CheckAsync(scheduled, Token));
        await _work.ExecuteAsync($"UPDATE isk_work.job_runs SET last_started_at = clock_timestamp() - interval '61 seconds' WHERE job = '{name}'");
        _stopped.Clear();

        // The check finds the job due; another worker then starts it before this start's guarded write.
        dispatcher.Units.AfterOpen = (operation, _) => operation == "work.job_start"
            ? _work.ExecuteAsync($"UPDATE isk_work.job_runs SET last_started_at = clock_timestamp() WHERE job = '{name}'")
            : Task.CompletedTask;

        Assert.Null(await runner.CheckAsync(scheduled, Token));
        Assert.DoesNotContain(_stopped, activity => activity.OperationName == "work.job" && (string?)activity.GetTagItem("iskender.work.job") == name);
    }

    public void Dispose() => _listener.Dispose();

    // Runs one pass of a dispatcher for the class's kind, and returns the dispatch activity of the item's one attempt.
    private async Task<Activity> DispatchAsync(Guid item)
    {
        await using (var dispatcher = new TestDispatcher(_work, [ApplyRecordingHandler.Registration(Traced)], WorkTestSettings.Fast))
        {
            await dispatcher.PassAsync();
        }

        Assert.Equal("completed|1", await _work.ItemAsync(item));
        return _stopped.Single(activity => activity.OperationName == "work.dispatch" && (string?)activity.GetTagItem("iskender.work.kind") == "applies.traced");
    }

    private Task<string> StoredAsync(Guid item, string column) =>
        _work.ScalarAsync<string>($"SELECT coalesce({column}, 'null') FROM isk_work.items WHERE item_id = '{item}'");

    private sealed class CompletingJob(string name) : IRecurringJob
    {
        public string Name => name;

        public ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken) => ValueTask.FromResult(RecurringJobOutcome.Completed);
    }
}
