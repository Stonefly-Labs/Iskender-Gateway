using System.Text;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Duplicate;
using Iskender.Work.Tests.Fixture;
using Microsoft.Extensions.Logging;

namespace Iskender.Work.Tests.Telemetry;

/// <summary>
/// Value-free telemetry (ISK-0009 T055, US7.3, SC-008, FR-005, FR-034): items whose scope IDs, idempotency keys, and
/// payloads carry the marker <see cref="Marker"/> go through every outcome — completed, retried, poisoned, skewed,
/// superseded, fencing rejected, lease lost, a job run, a malformed directory row, and a failed pass — and neither the
/// marker, nor the payload's hex or base64 text, nor any of the items' IDs appears in any log message, field, or
/// exception, span name or tag, metric label, or reported error the run has captured. Each event of the <c>work</c>
/// contract's Diagnostics fires in the outcome that names it. The worker's readiness answer is fixed text, asserted byte
/// for byte by the local environment's smoke (T058). The outcomes run in one test, one after another, so no step's
/// dispatcher claims another step's item.
/// </summary>
public class CanaryTests(WorkFixture work, TelemetryCapture capture) : IClassFixture<WorkFixture>
{
    /// <summary>Lowercase Crockford base32, so it is also the start of a valid organization ID body.</summary>
    private const string Marker = "wkcanary7z";

    private static readonly WorkKind Done = new("applies", "canary_done");
    private static readonly WorkKind Unknown = new("applies", "canary_unknown");
    private static readonly WorkKind Superseded = new("applies", "canary_superseded");

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes(Marker + "-payload");

    private readonly List<Guid> _items = [];
    private int _scopes;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoValueReachesTheTelemetryAndEachEventFiresInItsOutcome()
    {
        work.RequirePostgreSql();
        var before = work.Logger.Events.Count;
        var outbox = new PostgreSqlWorkOutbox(
            [new WorkKindDeclaration(Done, 1), new WorkKindDeclaration(Unknown, 1), new WorkKindDeclaration(Superseded, 1), new WorkKindDeclaration(TestKinds.FailOnce, 1), new WorkKindDeclaration(TestKinds.Record, 1), new WorkKindDeclaration(HangHandler.Kind, 1)],
            work.Logger);

        // Started, completed, skewed, stopped; and enqueued twice under one key, created then existing.
        var done = await EnqueueAsync(outbox, Done, "done");
        Assert.Equal(done, await EnqueueAsync(outbox, Done, "done", Scope(_scopes - 1)));
        await EnqueueAsync(outbox, Unknown, "skewed");
        await using (var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Done)], WorkTestSettings.Fast))
        {
            await using var startup = new WorkDispatcherStartup(() => a.Inner, capture.CreateLogger("Iskender.Work"));
            await startup.StartAsync(EmptySnapshot(), Token);
            await Eventually.StateAsync(work, done, "completed");
            await startup.StopAsync(Token);
        }

        // Retried, then completed; then poisoned.
        var retried = await EnqueueAsync(outbox, TestKinds.FailOnce, "retried");
        await using (var b = new TestDispatcher(work, [FailOnceHandler.Registration(maximumAttempts: 3)], WorkTestSettings.Fast))
        {
            await b.PassAsync();
            await work.MakeDueAsync(retried);
            await b.PassAsync();
        }

        Assert.Equal("completed|2", await work.ItemAsync(retried));
        var poisoned = await EnqueueAsync(outbox, TestKinds.FailOnce, "poisoned");
        await using (var c = new TestDispatcher(work, [FailOnceHandler.Registration(maximumAttempts: 1)], WorkTestSettings.Fast))
        {
            await c.PassAsync();
        }

        Assert.Equal("poisoned|1", await work.ItemAsync(poisoned));

        // Superseded: the attempt count moves on while the attempt is between its record and its apply.
        var superseded = await EnqueueAsync(outbox, Superseded, "superseded");
        await using (var d = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Superseded)], WorkTestSettings.Fast))
        {
            var held = d.Injection.At(WorkInjectionPoint.AfterAttemptCommit);
            var pass = d.StartPass();
            await held.Reached.WaitAsync(Eventually.Bound, Token);
            await work.ExecuteAsync($"UPDATE isk_work.items SET attempt_count = attempt_count + 1 WHERE item_id = '{superseded}'");
            held.Release();
            await pass;
        }

        await FinishAsync(superseded);

        // Fencing rejected: a stale holder's apply after another dispatcher took the shard and completed the item.
        var fenced = await EnqueueAsync(outbox, TestKinds.Record, "fenced");
        await using (var takeover = await Takeover.StartAsync(work))
        {
            takeover.HeldB.Release();
            await Eventually.StateAsync(work, fenced, "completed");
            takeover.HeldA.Release();
            await takeover.PassA;
        }

        // Lease lost: the lease row vanishes while the handler runs.
        var lost = await EnqueueAsync(outbox, HangHandler.Kind, "lost");
        await using (var e = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromMinutes(1))], WorkTestSettings.Fast))
        {
            var applying = e.Injection.At(WorkInjectionPoint.AfterAttemptCommit, blocks: false);
            var pass = e.StartPass();
            await applying.Reached.WaitAsync(Eventually.Bound, Token);
            await work.ExecuteAsync("DELETE FROM isk_coordination.leases");
            await pass;
        }

        await FinishAsync(lost);

        // A malformed directory row, and a job run.
        await work.ExecuteAsync("INSERT INTO isk_directory.scopes (listed_scope, module, due_at) VALUES ('org:not-an-id', 'work', clock_timestamp())");
        await using (var f = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Done)], WorkTestSettings.Fast))
        {
            await f.PassAsync();
            var interval = TimeSpan.FromMinutes(1);
            var scheduled = new ScheduledJob(new RecurringJobRegistration("applies.canary_job", interval, 1, typeof(CompletingJob)), interval, 1, f.Leases, new PostgreSqlFencingGuard(work.Logger));
            var runner = new JobRunner(f.Units, [scheduled], _ => new CompletingJob(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), work.Logger);
            Assert.Equal("completed", await runner.CheckAsync(scheduled, Token));
        }

        await work.ExecuteAsync("DELETE FROM isk_directory.scopes WHERE listed_scope = 'org:not-an-id'");

        // A failed pass: the database is unreachable.
        await using (var proxy = new OutageProxy(work.Postgres.Host, work.Postgres.Port) { Down = true })
        await using (var g = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Done)], WorkTestSettings.Fast, proxy))
        {
            g.Start();
            await Eventually.HoldsAsync(() => Task.FromResult(work.Logger.Events.Skip(before).Any(logged => logged.Id.Id == 8012)), "a failed pass");
        }

        var events = work.Logger.Events.Skip(before).Concat(capture.Logs).ToList();
        foreach (var (id, outcome) in new (int, string?)[]
        {
            (8001, null), (8002, null), (8003, "drained"), (8004, "created"), (8004, "existing"), (8005, null), (8006, null), (8007, null),
            (8008, "appeared"), (8009, null), (8010, WorkOutcomes.Superseded), (8010, WorkOutcomes.FencingRejected), (8010, WorkOutcomes.LeaseLost),
            (8011, "completed"), (8012, null),
        })
        {
            Assert.True(
                events.Any(logged => logged.Id.Id == id && (outcome is null || logged.Fields.Any(field => field.Key == "Outcome" && (string?)field.Value == outcome))),
                $"event {id} {outcome}");
        }

        Assert.Contains(events, logged => logged.Id.Id == 8006 && Field(logged, "Code") == "persistence.timeout");
        Assert.Contains(events, logged => logged.Id.Id == 8007 && Field(logged, "Code") == "persistence.timeout");
        Assert.Contains(events, logged => logged.Id.Id == 8012 && Field(logged, "Code") == "persistence.unavailable");
        Assert.Empty(capture.Hits(Found));
    }

    // Whether text holds the marker, the payload in any encoding a sink could write it in, or one of the items' IDs.
    private bool Found(string text) =>
        text.Contains(Marker, StringComparison.OrdinalIgnoreCase)
        || text.Contains(Convert.ToHexString(Payload), StringComparison.OrdinalIgnoreCase)
        || text.Contains(Convert.ToBase64String(Payload), StringComparison.Ordinal)
        || _items.Any(item => text.Contains(item.ToString("D"), StringComparison.OrdinalIgnoreCase) || text.Contains(item.ToString("N"), StringComparison.OrdinalIgnoreCase));

    private static string? Field(CapturedLog logged, string key) => logged.Fields.SingleOrDefault(field => field.Key == key).Value?.ToString();

    // A fresh organization whose ID body starts with the marker, so its shard leases are this step's own.
    private static UnitOfWorkScope Scope(int index) =>
        Owners.Organization(ResourceId.Parse($"org_0{Marker}{index:D15}", ResourceTypes.Organization));

    private async Task<Guid> EnqueueAsync(PostgreSqlWorkOutbox outbox, WorkKind kind, string key, UnitOfWorkScope? scope = null)
    {
        scope ??= Scope(_scopes++);
        await using var unit = await work.OpenAsync(scope);
        var item = (await outbox.EnqueueAsync(unit, new WorkItemRequest(scope.Owner, kind, 1, Payload, $"{Marker}-{key}"), Token)).ItemId;
        await unit.CommitAsync(Token);
        _items.Add(item);
        return item;
    }

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

    private sealed class CompletingJob : IRecurringJob
    {
        public string Name => "applies.canary_job";

        public ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken) => ValueTask.FromResult(RecurringJobOutcome.Completed);
    }
}
