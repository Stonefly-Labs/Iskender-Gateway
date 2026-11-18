using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Jobs;

/// <summary>
/// The recurring job runner (ISK-0009 T041, <c>work</c> contract Recurring jobs rules 1 to 3, research R10): the interval
/// is measured from <c>last_started_at</c>, a lease held elsewhere skips the run with 8011 <c>held</c>, the fenced
/// bookkeeping rejects a stale token, a <c>Partial</c> run keeps its cursor for the next, and a check that fails before
/// any run writes 8012 once per change. Each test uses a job name of
/// its own, because the bookkeeping row is the job's.
/// </summary>
public class JobRunnerTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheIntervalIsMeasuredFromTheLastStart()
    {
        work.RequirePostgreSql();
        await using var harness = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var (runner, job) = Runner(harness, "interval", (_, _) => ValueTask.FromResult(RecurringJobOutcome.Completed));

        Assert.Equal("completed", await runner.CheckAsync(job, Token));
        Assert.Null(await runner.CheckAsync(job, Token));

        // Started more than an interval ago, finished just now: due, because the interval counts from the start.
        await work.ExecuteAsync($"UPDATE isk_work.job_runs SET last_started_at = clock_timestamp() - interval '61 seconds', last_finished_at = clock_timestamp() WHERE job = '{job.Name}'");
        Assert.Equal("completed", await runner.CheckAsync(job, Token));
        Assert.Null(await runner.CheckAsync(job, Token));
        Assert.Equal(2, TestJob.Runs(job.Name));
        Assert.Equal(2, work.Logger.Events.Count(logged => logged.Id.Id == 8011 && Field(logged, "Job") == job.Name && Field(logged, "Outcome") == "completed"));
    }

    [Fact]
    public async Task ALeaseHeldElsewhereSkipsTheRunWithHeld()
    {
        work.RequirePostgreSql();
        await using var harness = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var (runner, job) = Runner(harness, "held", (_, _) => ValueTask.FromResult(RecurringJobOutcome.Completed));
        var other = (await harness.Leases.AcquireAsync(CoordinationScope.From(UnitOfWorkScope.Operator), job.Resource, TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;

        Assert.Equal("held", await runner.CheckAsync(job, Token));

        Assert.Equal(0, TestJob.Runs(job.Name));
        Assert.Single(work.Logger.Events, logged => logged.Id.Id == 8011 && Field(logged, "Job") == job.Name && Field(logged, "Outcome") == "held");
        await harness.Leases.ReleaseAsync(other, Token);
    }

    [Fact]
    public async Task TheFencedBookkeepingRejectsAStaleToken()
    {
        work.RequirePostgreSql();
        await using var harness = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var guard = new PostgreSqlFencingGuard(work.Logger);
        GatewayException? rejected = null;
        ScheduledJob? scheduled = null;
        var (runner, job) = Runner(harness, "stale", async (context, token) =>
        {
            // Another worker takes the lease over and fences the resource with its higher token.
            await work.ExecuteAsync($"DELETE FROM isk_coordination.leases WHERE scope_key = 'operator' AND key_hash = '{CoordinationKey.Hash(CoordinationScope.From(UnitOfWorkScope.Operator).Owner, scheduled!.Resource.Module, scheduled.Resource.Name)}'");
            var taken = (await harness.Leases.AcquireAsync(CoordinationScope.From(UnitOfWorkScope.Operator), scheduled.Resource, TimeSpan.FromMinutes(1), LeaseWait.TryOnly, token)).Lease!;
            await using (var unit = await harness.Units.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions("work", "test.takeover"), token))
            {
                await guard.AcceptAsync(unit, scheduled.Resource, taken, token);
                await unit.CommitAsync(token);
            }

            rejected = await Assert.ThrowsAsync<GatewayException>(async () => await context.SaveCursorAsync("page-1", token));
            await harness.Leases.ReleaseAsync(taken, token);
            throw rejected;
        });
        scheduled = job;

        Assert.Equal("failed", await runner.CheckAsync(job, Token));

        Assert.Same(ErrorCodes.CoordinationFencingRejected, rejected!.Code);
        Assert.Contains(work.Logger.Events, logged => logged.Id.Id == 5006);
        Assert.Equal("null", await work.ScalarAsync<string>($"SELECT coalesce(cursor, 'null') FROM isk_work.job_runs WHERE job = '{job.Name}'"));
        Assert.Single(work.Logger.Events, logged => logged.Id.Id == 8011 && Field(logged, "Job") == job.Name && Field(logged, "Outcome") == "failed");
    }

    [Fact]
    public async Task APartialRunKeepsItsCursorAndTheNextRunResumesThere()
    {
        work.RequirePostgreSql();
        await using var harness = new TestDispatcher(work, [], WorkTestSettings.Fast);
        var cursors = new List<string?>();
        var (runner, job) = Runner(harness, "cursor", async (context, token) =>
        {
            cursors.Add(context.Cursor);
            if (context.Cursor is null)
            {
                await context.SaveCursorAsync("page-1", token);
                return RecurringJobOutcome.Partial;
            }

            return RecurringJobOutcome.Completed;
        });

        Assert.Equal("partial", await runner.CheckAsync(job, Token));
        Assert.Equal("page-1", await work.ScalarAsync<string>($"SELECT cursor FROM isk_work.job_runs WHERE job = '{job.Name}'"));

        await work.ExecuteAsync($"UPDATE isk_work.job_runs SET last_started_at = clock_timestamp() - interval '61 seconds' WHERE job = '{job.Name}'");
        Assert.Equal("completed", await runner.CheckAsync(job, Token));

        Assert.Equal([null, "page-1"], cursors);
        Assert.Equal("null", await work.ScalarAsync<string>($"SELECT coalesce(cursor, 'null') FROM isk_work.job_runs WHERE job = '{job.Name}'"));
        Assert.True(await work.ScalarAsync<bool>($"SELECT last_finished_at >= last_started_at FROM isk_work.job_runs WHERE job = '{job.Name}'"));
    }

    // A check that fails before any run, here on an unreachable database, writes 8012 once per change of class and code,
    // and again after a check succeeds (ISK-0009 T069, Diagnostics rule 2, FR-033).
    [Fact]
    public async Task AnUnreachableDatabaseWrites8012OncePerChangeAndAgainAfterASuccess()
    {
        work.RequirePostgreSql();
        await using var proxy = new OutageProxy(work.Postgres.Host, work.Postgres.Port);
        await using var harness = new TestDispatcher(work, [], WorkTestSettings.Fast, proxy);
        var (runner, job) = Runner(harness, "outage", (_, _) => ValueTask.FromResult(RecurringJobOutcome.Completed));
        var before = work.Logger.Events.Count;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        proxy.Down = true;
        var running = runner.RunAsync(stop.Token);
        try
        {
            var checks = Checks(harness);
            await Eventually.HoldsAsync(() => Task.FromResult(Checks(harness) >= checks + 3), "three failed checks");
            var failure = Assert.Single(JobFailures(before));
            Assert.Equal(("transient", "persistence.unavailable"), (Field(failure, "FailureClass"), Field(failure, "Code")));
            Assert.Equal(0, TestJob.Runs(job.Name));

            // A check that succeeds, then a second outage, writes 8012 again.
            proxy.Down = false;
            await Eventually.HoldsAsync(() => Task.FromResult(TestJob.Runs(job.Name) == 1), "a run after the outage");
            checks = Checks(harness);
            await Eventually.HoldsAsync(() => Task.FromResult(Checks(harness) > checks), "a check after the run");
            proxy.Down = true;
            await Eventually.HoldsAsync(() => Task.FromResult(JobFailures(before).Count == 2), "8012 after the success");
        }
        finally
        {
            proxy.Down = false;
            await stop.CancelAsync();
            await running;
        }
    }

    private static int Checks(TestDispatcher harness) => harness.Units.Opened.Count(opened => opened.Operation == "work.job_check");

    private List<CapturedLog> JobFailures(int before) => [.. work.Logger.Events.Skip(before).Where(logged => logged.Id.Id == 8012)];

    private static string? Field(CapturedLog logged, string name) => logged.Fields.FirstOrDefault(field => field.Key == name).Value?.ToString();

    // A runner over one job named applies.job_<suffix>_<unique>, with a 1 min interval and batch 10, running behavior.
    private (JobRunner Runner, ScheduledJob Job) Runner(TestDispatcher harness, string suffix, Func<RecurringJobContext, CancellationToken, ValueTask<RecurringJobOutcome>> behavior)
    {
        var name = $"applies.job_{suffix}_{Guid.NewGuid():N}"[..40];
        TestJob.Behaviors[name] = behavior;
        var job = new ScheduledJob(new RecurringJobRegistration(name, TimeSpan.FromMinutes(1), 10, typeof(TestJob)), TimeSpan.FromMinutes(1), 10, harness.Leases, new PostgreSqlFencingGuard(work.Logger));
        var runner = new JobRunner(harness.Units, [job], _ => new TestJob(name), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), work.Logger);
        return (runner, job);
    }

    private sealed class TestJob(string name) : IRecurringJob
    {
        public static System.Collections.Concurrent.ConcurrentDictionary<string, Func<RecurringJobContext, CancellationToken, ValueTask<RecurringJobOutcome>>> Behaviors { get; } = new();

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Counts = new();

        public string Name => name;

        public static int Runs(string name) => Counts.GetValueOrDefault(name);

        public ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken)
        {
            Counts.AddOrUpdate(name, 1, (_, count) => count + 1);
            return Behaviors[name](context, cancellationToken);
        }
    }
}
