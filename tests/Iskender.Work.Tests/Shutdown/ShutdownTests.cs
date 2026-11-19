using System.Diagnostics;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;
using Microsoft.Extensions.Hosting;

namespace Iskender.Work.Tests.Shutdown;

/// <summary>
/// Shutdown in process (ISK-0009 T049, research R12, <c>work</c> contract Shutdown, US6.2): after the host's stop signal
/// nothing more is claimed, a short item completes, a long item is cancelled at the deadline and stays <c>pending</c> with
/// its lease released, so a second dispatcher takes it as soon as it is due, the stop ends within the deadline plus 5
/// seconds, an item that becomes due during the stop is left for the next dispatcher, and a job that falls due after the
/// signal does not start.
/// </summary>
public class ShutdownTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASignalDuringStartUpClaimsNothing()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "signal-at-start");
        await using var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast);
        using var lifetime = new TestLifetime();
        lifetime.StopApplication();
        await using var startup = Startup(a, lifetime);

        await startup.StartAsync(EmptySnapshot(), Token);
        await startup.StopAsync(Token);

        Assert.Equal("pending|0", await work.ItemAsync(item));
        Assert.DoesNotContain(a.Units.Opened, opened => opened.Operation == "work.claim");
        await FinishAsync(item);
    }

    [Fact]
    public async Task AfterTheSignalNothingMoreIsClaimed()
    {
        work.RequirePostgreSql();
        await using var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast);
        using var lifetime = new TestLifetime();
        await using var startup = Startup(a, lifetime);
        await startup.StartAsync(EmptySnapshot(), Token);
        await Eventually.HoldsAsync(() => Task.FromResult(a.Units.Opened.Any(opened => opened.Operation == "work.directory_page")), "a first pass");

        lifetime.StopApplication();
        var opened = a.Units.Opened.Count;
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "after-signal");
        await startup.StopAsync(Token);

        Assert.Equal("pending|0", await work.ItemAsync(item));
        Assert.DoesNotContain(a.Units.Opened.Skip(opened), unit => unit.Operation == "work.claim");
        Assert.Equal("drained", Outcome());
        await FinishAsync(item);
    }

    [Fact]
    public async Task AShortItemInFlightAtTheSignalCompletes()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "short");
        await using var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast);
        var hook = a.Injection.At(WorkInjectionPoint.AfterAttemptCommit);
        using var lifetime = new TestLifetime();
        await using var startup = Startup(a, lifetime);
        await startup.StartAsync(EmptySnapshot(), Token);
        await hook.Reached.WaitAsync(Eventually.Bound, Token);

        lifetime.StopApplication();
        hook.Release();
        await startup.StopAsync(Token);

        Assert.Equal("completed|1", await work.ItemAsync(item));
        Assert.Equal("drained", Outcome());
    }

    [Fact]
    public async Task ALongItemIsCancelledAtTheDeadlineReleasedAndTakenByTheNextDispatcherAsSoonAsDue()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var item = await work.EnqueueAsync(scope, HangHandler.Kind, "long");
        await using var a = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromMinutes(5))], WorkTestSettings.Fast);
        var hook = a.Injection.At(WorkInjectionPoint.AfterAttemptCommit, blocks: false);
        using var lifetime = new TestLifetime();
        await using var startup = Startup(a, lifetime);
        await startup.StartAsync(EmptySnapshot(), Token);
        await hook.Reached.WaitAsync(Eventually.Bound, Token);
        await Eventually.HoldsAsync(async () => await work.AppliesAsync(item) == 0 && a.Inner.InFlight == 1, "the handler running");

        var stopping = Stopwatch.StartNew();
        lifetime.StopApplication();
        var due = await work.EnqueueAsync(scope, TestKinds.Record, "due-during-stop");
        await startup.StopAsync(Token);
        stopping.Stop();

        Assert.InRange(stopping.Elapsed, ShutdownTimeout - TimeSpan.FromMilliseconds(100), ShutdownTimeout + TimeSpan.FromSeconds(5));
        Assert.Equal("cancelled", Outcome());
        Assert.Equal("pending|1", await work.ItemAsync(item));
        Assert.Equal("transient|work.handler_cancelled", await work.ScalarAsync<string>($"SELECT last_failure_class || '|' || last_failure_code FROM isk_work.items WHERE item_id = '{item}'"));
        Assert.Equal(0L, await work.AppliesAsync(item));
        Assert.Equal(0L, await work.ScalarAsync<long>("SELECT count(*) FROM isk_coordination.leases WHERE expires_at > clock_timestamp()"));
        Assert.Equal("pending|0", await work.ItemAsync(due));

        // The next dispatcher takes both on its first pass once they are due: the shard lease is free, not waiting to expire.
        await Eventually.HoldsAsync(
            async () => await work.ScalarAsync<bool>($"SELECT due_at <= clock_timestamp() FROM isk_work.items WHERE item_id = '{item}'"),
            "the cancelled item to fall due");
        await using var b = new TestDispatcher(work, [ApplyRecordingHandler.Registration(HangHandler.Kind), ApplyRecordingHandler.Registration()], WorkTestSettings.Fast);
        await b.PassAsync();

        Assert.Equal("completed|2", await work.ItemAsync(item));
        Assert.Equal("completed|1", await work.ItemAsync(due));
    }

    // A healthy handler cancelled at the deadline on its item's last allowed attempt leaves the item pending, with its
    // attempt recorded as transient / work.handler_cancelled, and the next claim poisons it without running it, as after a
    // crash (ISK-0009 T087, Shutdown rule 2, Retry and poison rule 4, TR-13; decided by the maintainer in implement on
    // 2026-11-19).
    [Fact]
    public async Task ALastAttemptCancelledAtTheDeadlineStaysPendingUntilTheNextClaim()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), HangHandler.Kind, "last");
        var before = work.Logger.Events.Count;
        await using (var a = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromMinutes(5), maximumAttempts: 1)], WorkTestSettings.Fast))
        {
            var hook = a.Injection.At(WorkInjectionPoint.AfterAttemptCommit, blocks: false);
            using var lifetime = new TestLifetime();
            await using var startup = Startup(a, lifetime);
            await startup.StartAsync(EmptySnapshot(), Token);
            await hook.Reached.WaitAsync(Eventually.Bound, Token);
            await Eventually.HoldsAsync(() => Task.FromResult(a.Inner.InFlight == 1), "the handler running");

            lifetime.StopApplication();
            await startup.StopAsync(Token);
            Assert.Equal("cancelled", Outcome());
        }

        Assert.Equal("pending|1", await work.ItemAsync(item));
        Assert.Equal("transient|work.handler_cancelled", await work.ScalarAsync<string>($"SELECT last_failure_class || '|' || last_failure_code FROM isk_work.items WHERE item_id = '{item}'"));
        Assert.DoesNotContain(work.Logger.Events.Skip(before), logged => logged.Id.Id == 8007);

        // The next claim finds the item at its maximum and poisons it without running it.
        await Eventually.HoldsAsync(
            async () => await work.ScalarAsync<bool>($"SELECT due_at <= clock_timestamp() FROM isk_work.items WHERE item_id = '{item}'"),
            "the cancelled item to fall due");
        await using (var b = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromMinutes(5), maximumAttempts: 1)], WorkTestSettings.Fast))
        {
            await b.PassAsync();
        }

        Assert.Equal("poisoned|1", await work.ItemAsync(item));
        Assert.Equal("transient|work.handler_cancelled", await work.ScalarAsync<string>($"SELECT last_failure_class || '|' || last_failure_code FROM isk_work.items WHERE item_id = '{item}'"));
        Assert.Single(work.Logger.Events.Skip(before), logged => logged.Id.Id == 8007 && Field(logged, "Code") == "work.handler_cancelled");
        Assert.Equal(0L, await work.AppliesAsync(item));
    }

    // A registered job that falls due after the signal does not start, and its lease is free afterwards (ISK-0009 T073,
    // Shutdown rule 1).
    [Fact]
    public async Task AJobThatFallsDueAfterTheSignalDoesNotStart()
    {
        work.RequirePostgreSql();
        await using var a = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var name = $"applies.shutdown_{Guid.NewGuid():N}"[..40];
        var interval = TimeSpan.FromMinutes(1);
        var scheduled = new ScheduledJob(new RecurringJobRegistration(name, interval, 1, typeof(CountingJob)), interval, 1, a.Leases, new PostgreSqlFencingGuard(work.Logger));
        var runner = new JobRunner(a.Units, [scheduled], _ => new CountingJob(name), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), work.Logger);
        await work.ExecuteAsync($"INSERT INTO isk_work.job_runs (scope_key, job, last_started_at) VALUES ('operator', '{name}', clock_timestamp())");
        using var lifetime = new TestLifetime();
        await using var startup = new WorkDispatcherStartup(() => a.Inner, work.Logger, createJobs: () => runner, lifetime: () => lifetime, shutdownTimeout: () => ShutdownTimeout);
        await startup.StartAsync(EmptySnapshot(), Token);
        await Eventually.HoldsAsync(() => Task.FromResult(a.Units.Opened.Any(opened => opened.Operation == "work.job_check")), "a first job check");

        // The job falls due 2 s after the signal; the stop waits until two poll intervals past that on the database clock.
        lifetime.StopApplication();
        var opened = a.Units.Opened.Count;
        await work.ExecuteAsync($"UPDATE isk_work.job_runs SET last_started_at = clock_timestamp() - interval '58 seconds' WHERE job = '{name}'");
        await Eventually.HoldsAsync(
            async () => await work.ScalarAsync<bool>($"SELECT last_started_at + interval '62 seconds' <= clock_timestamp() FROM isk_work.job_runs WHERE job = '{name}'"),
            "the job to have been due for two poll intervals");
        await startup.StopAsync(Token);

        Assert.Equal(0, CountingJob.Runs(name));
        Assert.DoesNotContain(a.Units.Opened.Skip(opened), unit => unit.Operation is "work.job_check" or "work.job_start");
        Assert.True(await work.ScalarAsync<bool>($"SELECT last_finished_at IS NULL FROM isk_work.job_runs WHERE job = '{name}'"));
        Assert.Equal(0L, await work.ScalarAsync<long>("SELECT count(*) FROM isk_coordination.leases WHERE expires_at > clock_timestamp()"));
    }

    // A job run in flight at the signal continues until the deadline, like an in-flight item: one that finishes before it
    // records its finish and 8011 completed, and its lease is free afterwards (ISK-0009 T080, FR-027, Shutdown rule 2).
    [Fact]
    public async Task AJobRunInFlightAtTheSignalFinishesBeforeTheDeadline()
    {
        work.RequirePostgreSql();
        await using var a = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var name = $"applies.inflight_{Guid.NewGuid():N}"[..40];
        var interval = TimeSpan.FromMinutes(1);
        var job = new GatedJob();
        var scheduled = new ScheduledJob(new RecurringJobRegistration(name, interval, 1, typeof(GatedJob)), interval, 1, a.Leases, new PostgreSqlFencingGuard(work.Logger));
        var runner = new JobRunner(a.Units, [scheduled], _ => job, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), work.Logger);
        using var lifetime = new TestLifetime();
        await using var startup = new WorkDispatcherStartup(() => a.Inner, work.Logger, createJobs: () => runner, lifetime: () => lifetime, shutdownTimeout: () => ShutdownTimeout);
        await startup.StartAsync(EmptySnapshot(), Token);
        await job.Started.Task.WaitAsync(Eventually.Bound, Token);

        lifetime.StopApplication();
        var stopping = startup.StopAsync(Token).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(500), Token);
        Assert.False(stopping.IsCompleted, "The stop finished before the job run.");
        job.Finish.SetResult();
        await stopping;

        Assert.False(job.Cancelled, "The job run was cancelled at the signal.");
        Assert.True(await work.ScalarAsync<bool>($"SELECT last_finished_at IS NOT NULL FROM isk_work.job_runs WHERE job = '{name}'"));
        Assert.Single(work.Logger.Events, logged => logged.Id.Id == 8011 && Field(logged, "Job") == name && Field(logged, "Outcome") == "completed");
        Assert.DoesNotContain(work.Logger.Events, logged => logged.Id.Id == 8011 && Field(logged, "Job") == name && Field(logged, "Outcome") == "failed");
        Assert.Equal("drained", Outcome());
        Assert.Equal(0L, await work.ScalarAsync<long>("SELECT count(*) FROM isk_coordination.leases WHERE expires_at > clock_timestamp()"));
    }

    // A job run that swallows its cancellation at the deadline, and whose lease release then hangs, has its release
    // abandoned 2 seconds after the deadline, so the stop still ends then (ISK-0009 T090, Shutdown rules 3 and 4).
    [Fact]
    public async Task AJobRunWhoseReleaseHangsIsAbandonedTwoSecondsAfterTheDeadline()
    {
        work.RequirePostgreSql();
        await using var a = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var name = $"applies.release_{Guid.NewGuid():N}"[..40];
        var interval = TimeSpan.FromMinutes(1);
        var job = new SwallowingJob();
        var scheduled = new ScheduledJob(new RecurringJobRegistration(name, interval, 1, typeof(SwallowingJob)), interval, 1, a.Leases, new PostgreSqlFencingGuard(work.Logger));
        var runner = new JobRunner(a.Units, [scheduled], _ => job, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), work.Logger);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        a.Leases.HoldRelease = held.Task;
        using var lifetime = new TestLifetime();
        await using var startup = new WorkDispatcherStartup(() => a.Inner, work.Logger, createJobs: () => runner, lifetime: () => lifetime, shutdownTimeout: () => ShutdownTimeout);
        await startup.StartAsync(EmptySnapshot(), Token);
        await job.Started.Task.WaitAsync(Eventually.Bound, Token);

        var stopping = Stopwatch.StartNew();
        lifetime.StopApplication();
        await startup.StopAsync(Token).AsTask().WaitAsync(Eventually.Bound, Token);
        stopping.Stop();
        held.TrySetResult();

        Assert.True(job.Cancelled, "The job run was not cancelled at the deadline.");
        Assert.InRange(stopping.Elapsed, ShutdownTimeout + TimeSpan.FromSeconds(2) - TimeSpan.FromMilliseconds(100), ShutdownTimeout + TimeSpan.FromSeconds(2) + TimeSpan.FromSeconds(1));
        Assert.Equal("cancelled", Outcome());
        Assert.True(await work.ScalarAsync<bool>($"SELECT last_finished_at IS NOT NULL FROM isk_work.job_runs WHERE job = '{name}'"));

        // The abandoned release left the job's lease to expire on its own; later tests of the class expect none held.
        await work.ExecuteAsync("DELETE FROM isk_coordination.leases");
    }

    private WorkDispatcherStartup Startup(TestDispatcher dispatcher, TestLifetime lifetime) =>
        new(() => dispatcher.Inner, work.Logger, lifetime: () => lifetime, shutdownTimeout: () => ShutdownTimeout);

    // The outcome of this test's 8003; the class's tests run one at a time, so the last one is this test's.
    private string? Outcome() =>
        work.Logger.Events.Last(logged => logged.Id.Id == 8003).Fields.Single(field => field.Key == "Outcome").Value?.ToString();

    private static string? Field(CapturedLog logged, string name) => logged.Fields.FirstOrDefault(field => field.Key == name).Value?.ToString();

    private Task FinishAsync(Guid item) =>
        work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{item}' AND state = 'pending'");

    private static ConfigurationSnapshot EmptySnapshot()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([WorkSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("empty.json", ConfigurationDocumentFormat.Json, "{}"),
            environmentName: "development"));
        Assert.True(report.IsValid, report.ToString());
        return report.Snapshot!;
    }

    // A job that counts its runs, by name.
    private sealed class CountingJob(string name) : IRecurringJob
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Counts = new();

        public string Name => name;

        public static int Runs(string name) => Counts.GetValueOrDefault(name);

        public ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken)
        {
            Counts.AddOrUpdate(name, 1, (_, count) => count + 1);
            return ValueTask.FromResult(RecurringJobOutcome.Completed);
        }
    }

    // A job whose run signals that it started, then waits for the test to let it finish, noting whether it was cancelled.
    private sealed class GatedJob : IRecurringJob
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        public string Name => "gated";

        public async ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Finish.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }

            return RecurringJobOutcome.Completed;
        }
    }

    // A job whose run signals that it started, then waits until it is cancelled and ends as partial instead of throwing,
    // so its finish write and its lease release run after the deadline.
    private sealed class SwallowingJob : IRecurringJob
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        public string Name => "swallowing";

        public async ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
            }

            return RecurringJobOutcome.Partial;
        }
    }

    // The host's lifetime, whose StopApplication is the stop signal.
    private sealed class TestLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose() => _stopping.Dispose();
    }
}
