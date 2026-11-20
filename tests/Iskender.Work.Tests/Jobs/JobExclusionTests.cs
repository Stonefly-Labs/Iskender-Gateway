using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Jobs;

/// <summary>
/// Job exclusion and resumption across dispatchers (ISK-0009 T044, US5.2, US5.3, FR-019, FR-022): of two dispatchers
/// whose job falls due together, exactly one runs it and the other writes 8011 <c>held</c>; and a dispatcher abandoned
/// mid-purge or mid-retention leaves its cursor at its last page, and the next run finishes with no error and no change
/// beyond deleting expired or retained rows.
/// </summary>
public class JobExclusionTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private const string Coordination =
        "\"leases\":{\"coordinator\":\"postgresql\",\"minimumDuration\":\"300ms\",\"maximumDuration\":\"1m\",\"safetyMargin\":\"100ms\"}";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OfTwoDispatchersWhoseJobFallsDueTogetherOneRunsItAndTheOtherIsHeld()
    {
        work.RequirePostgreSql();
        await using var a = new TestDispatcher(work, [], WorkTestSettings.Fast);
        await using var b = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var name = $"applies.exclusive_{Guid.NewGuid():N}"[..40];
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        var job = new DelegateJob(name, async (_, token) =>
        {
            Interlocked.Increment(ref runs);
            started.TrySetResult();
            await finish.Task.WaitAsync(token);
            return RecurringJobOutcome.Completed;
        });
        var gated = new GatedLeases(b.Leases);
        var (runnerA, scheduledA) = Runner(a.Units, a.Leases, job);
        var (runnerB, scheduledB) = Runner(b.Units, gated, job);

        // B finds the job due and stops before its lease; A then finds it due, takes the lease, and starts the run.
        var checkB = runnerB.CheckAsync(scheduledB, Token);
        await gated.Reached.Task.WaitAsync(Eventually.Bound, Token);
        var checkA = runnerA.CheckAsync(scheduledA, Token);
        await started.Task.WaitAsync(Eventually.Bound, Token);
        gated.Open();

        Assert.Equal("held", await checkB.WaitAsync(Eventually.Bound, Token));
        finish.SetResult();
        Assert.Equal("completed", await checkA.WaitAsync(Eventually.Bound, Token));
        Assert.Equal(1, runs);
        Assert.Single(work.Logger.Events, logged => logged.Id.Id == 8011 && Field(logged, "Job") == name && Field(logged, "Outcome") == "held");
        Assert.Single(work.Logger.Events, logged => logged.Id.Id == 8011 && Field(logged, "Job") == name && Field(logged, "Outcome") == "completed");
    }

    [Fact]
    public async Task APurgeAbandonedMidRunLeavesItsCursorAndTheNextRunOnlyFinishesTheDeletes()
    {
        work.RequirePostgreSql();
        var (_, settings) = WorkTestSettings.Read(coordination: Coordination);
        var scopes = Enumerable.Range(0, 3).Select(_ => CoordinationScope.From(WorkFixture.FreshOrganization())).ToList();
        await using (var setup = new TestDispatcher(work, [], WorkTestSettings.Fast))
        {
            var atomic = new PostgreSqlAtomicOperations(setup.Units, settings, work.Logger);
            foreach (var scope in scopes)
            {
                await atomic.AddAsync(scope, new CoordinationName("abandon", "expired"), 1, TimeSpan.FromMilliseconds(300), Token);
                await atomic.AddAsync(scope, new CoordinationName("abandon", "live"), 1, TimeSpan.FromMinutes(5), Token);
            }
        }

        await Eventually.HoldsAsync(async () => await ValuesAsync(scopes, "<= clock_timestamp()") == 3, "the brief values to expire");
        var live = await ValuesAsync(scopes, "> clock_timestamp()");

        await AbandonThenResumeAsync(WorkSettings.PurgeExpiredJobName, units => new PurgeExpiredJob(units, settings));

        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.coordination_values WHERE expires_at <= clock_timestamp() AND scope_key IN ({Keys(scopes)})"));
        Assert.Equal(live, await ValuesAsync(scopes, "> clock_timestamp()"));
        Assert.Equal(3L, live);
    }

    [Fact]
    public async Task ARetentionAbandonedMidRunLeavesItsCursorAndTheNextRunOnlyFinishesTheDeletes()
    {
        work.RequirePostgreSql();
        var (settings, _) = WorkTestSettings.Read();
        var old = new List<Guid>();
        var kept = new List<Guid>();
        foreach (var scope in Enumerable.Range(0, 3).Select(_ => WorkFixture.FreshOrganization()))
        {
            var item = await work.EnqueueAsync(scope, TestKinds.Record, "old");
            await work.ExecuteAsync($"UPDATE isk_work.items SET state = 'completed', attempt_count = 1, finished_at = clock_timestamp() - interval '8 days' WHERE item_id = '{item}'");
            old.Add(item);
            kept.Add(await work.EnqueueAsync(scope, TestKinds.Record, "pending"));
        }

        await AbandonThenResumeAsync(WorkSettings.RetentionJobName, units => new WorkRetentionJob(units, settings));

        foreach (var item in old)
        {
            Assert.Equal("absent", await work.ItemAsync(item));
        }

        foreach (var item in kept)
        {
            Assert.Equal("pending|0", await work.ItemAsync(item));
        }
    }

    private static string? Field(CapturedLog logged, string name) => logged.Fields.FirstOrDefault(field => field.Key == name).Value?.ToString();

    private static string Keys(IEnumerable<CoordinationScope> scopes) => string.Join(", ", scopes.Select(scope => $"'{CoordinationKey.ScopeKey(scope.Owner)}'"));

    private Task<long> ValuesAsync(IEnumerable<CoordinationScope> scopes, string expiry) =>
        work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.coordination_values WHERE expires_at {expiry} AND scope_key IN ({Keys(scopes)})");

    // Runs the shipped job in dispatcher A, a page at a time, abandoning A right after its first cursor is saved; then
    // checks the cursor is that page's, and runs the job to completion in dispatcher B once A's lease has expired.
    private async Task AbandonThenResumeAsync(string jobName, Func<IUnitOfWorkFactory, IRecurringJob> create)
    {
        await work.ExecuteAsync($"DELETE FROM isk_work.job_runs WHERE job = '{jobName}'");
        string? first = null;
        await using (var a = new TestDispatcher(work, [], WorkTestSettings.Fast))
        {
            var abandoning = new DelegateJob(jobName, async (context, token) =>
            {
                var inner = create(a.Units);
                var saving = new RecurringJobContext(1, context.Cursor, async (cursor, saveToken) =>
                {
                    await context.SaveCursorAsync(cursor, saveToken);
                    first = cursor;
                    await a.AbandonAsync();
                    throw new OperationCanceledException("The dispatcher was abandoned.");
                });
                return await inner.RunAsync(saving, token);
            });
            var (runner, scheduled) = Runner(a.Units, a.Leases, abandoning, batch: 1);
            try
            {
                await runner.CheckAsync(scheduled, Token);
            }
            catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
            {
                // The abandoned dispatcher's own failure; only what it left in the database matters.
            }
        }

        Assert.NotNull(first);
        Assert.Equal(first, await work.ScalarAsync<string>($"SELECT cursor FROM isk_work.job_runs WHERE job = '{jobName}'"));
        await work.ExecuteAsync($"UPDATE isk_work.job_runs SET last_started_at = clock_timestamp() - interval '61 seconds' WHERE job = '{jobName}'");

        await using var b = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var resumed = new List<string?>();
        var resuming = new DelegateJob(jobName, (context, token) =>
        {
            resumed.Add(context.Cursor);
            return create(b.Units).RunAsync(context, token);
        });
        var (next, job) = Runner(b.Units, b.Leases, resuming, batch: 1);

        // A's lease outlives it by at most its 5 s duration, until which B is held.
        string? outcome = null;
        await Eventually.HoldsAsync(async () => (outcome = await next.CheckAsync(job, Token)) != "held", "the next run");
        Assert.Equal("completed", outcome);
        Assert.Equal([first], resumed);
        Assert.Equal("null", await work.ScalarAsync<string>($"SELECT coalesce(cursor, 'null') FROM isk_work.job_runs WHERE job = '{jobName}'"));
    }

    private (JobRunner Runner, ScheduledJob Job) Runner(IUnitOfWorkFactory units, ILeaseCoordinator leases, DelegateJob job, int batch = 10)
    {
        var scheduled = new ScheduledJob(new RecurringJobRegistration(job.Name, Interval, batch, typeof(DelegateJob)), Interval, batch, leases, new PostgreSqlFencingGuard(work.Logger));
        return (new JobRunner(units, [scheduled], _ => job, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), work.Logger), scheduled);
    }

    private sealed class DelegateJob(string name, Func<RecurringJobContext, CancellationToken, ValueTask<RecurringJobOutcome>> run) : IRecurringJob
    {
        public string Name => name;

        public ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken) => run(context, cancellationToken);
    }

    // Holds each acquire until the test opens the gate, signalling when one arrives.
    private sealed class GatedLeases(ILeaseCoordinator inner) : ILeaseCoordinator
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _open.TrySetResult();

        public async ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
        {
            Reached.TrySetResult();
            await _open.Task.WaitAsync(cancellationToken);
            return await inner.AcquireAsync(scope, name, duration, wait, cancellationToken);
        }

        public ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken) => inner.RenewAsync(lease, duration, cancellationToken);

        public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken) => inner.ReleaseAsync(lease, cancellationToken);
    }
}
