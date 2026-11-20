using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Skew;

/// <summary>
/// Version skew (ISK-0009 T053, research R8, <c>work</c> contract Version skew, US7.2, FR-015, FR-018): an item of a kind
/// this worker has no handler for, or whose payload version is above its handler's highest, stays <c>pending</c> with its
/// attempt count unchanged, is reported by <c>iskender.work.items.skewed</c> and event 8008, and never takes a handled
/// item's place in a batch; a dispatcher that handles it picks it up at once. Skew spanning several directory pages is
/// counted over the whole cursor cycle. The class's kinds are its own, so other
/// classes' gauges and events never match.
/// </summary>
public class SkewTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static readonly WorkKind Unknown = new("applies", "skew_unknown");

    private static readonly WorkKind Versioned = new("applies", "skew_versioned");

    private static readonly WorkKind Handled = new("applies", "skew_handled");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnknownKindAndAPayloadVersionTooHighStayPendingAndAreReported()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var unknown = await EnqueueAsync(scope, Unknown, 1, "unknown");
        var tooHigh = await EnqueueAsync(scope, Versioned, 2, "too-high");
        var handled = await EnqueueAsync(scope, Versioned, 1, "handled");
        var before = work.Logger.Events.Count;
        Dictionary<string, long> skewed;
        Dictionary<string, long> backlog;

        await using (var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Versioned)], WorkTestSettings.Fast))
        {
            (skewed, backlog) = await PassAndObserveAsync(a);
            await a.PassAsync();
        }

        Assert.Equal("pending|0", await work.ItemAsync(unknown));
        Assert.Equal("pending|0", await work.ItemAsync(tooHigh));
        Assert.Equal("completed|1", await work.ItemAsync(handled));
        Assert.Equal(0L, await work.AppliesAsync(unknown) + await work.AppliesAsync(tooHigh));
        Assert.Equal((1L, 1L), (skewed.GetValueOrDefault(Unknown.ToString()), skewed.GetValueOrDefault(Versioned.ToString())));
        Assert.Equal(1L, backlog.GetValueOrDefault(Versioned.ToString()));
        // Only this test's events: other tests of the class report these kinds appearing and clearing too.
        Assert.Equal(["appeared"], Skew(Unknown, before));
        Assert.Equal(["appeared"], Skew(Versioned, before));
    }

    [Fact]
    public async Task ABatchSkewedItemsWouldFillStillDispatchesTheHandledItems()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        foreach (var index in Enumerable.Range(0, 3))
        {
            await EnqueueAsync(scope, Unknown, 1, $"filler-{index}");
        }

        var handled = await EnqueueAsync(scope, Handled, 1, "handled");

        // Other tests of the class leave skewed scopes due, so one whole cycle of the directory cursor reaches this scope's page.
        var due = await work.ScalarAsync<long>("SELECT count(*) FROM isk_directory.scopes WHERE module = 'work' AND due_at <= clock_timestamp()");
        await using (var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Handled)], WorkTestSettings.Fast + ",\"batchSize\":2"))
        {
            foreach (var pass in Enumerable.Range(0, (int)((due / 2) + 1)))
            {
                await a.PassAsync();
            }
        }

        Assert.Equal("completed|1", await work.ItemAsync(handled));
    }

    [Fact]
    public async Task ADispatcherThatHandlesTheKindPicksTheItemsUpAtOnceAndTheSkewClears()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var unknown = await EnqueueAsync(scope, Unknown, 1, "picked-unknown");
        var tooHigh = await EnqueueAsync(scope, Versioned, 2, "picked-too-high");
        await using var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Versioned)], WorkTestSettings.Fast);
        await a.PassAsync();
        var appeared = (Skew(Unknown).Count, Skew(Versioned).Count);

        await using (var b = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Unknown), new WorkHandlerRegistration(Versioned, 1, 2, null, null, hasExternalEffects: false, typeof(ApplyRecordingHandler))], WorkTestSettings.Fast))
        {
            await b.PassAsync();
        }

        Assert.Equal("completed|1", await work.ItemAsync(unknown));
        Assert.Equal("completed|1", await work.ItemAsync(tooHigh));
        var (skewed, _) = await PassAndObserveAsync(a);
        Assert.Equal(0L, skewed.GetValueOrDefault(Unknown.ToString()) + skewed.GetValueOrDefault(Versioned.ToString()));
        Assert.Equal("cleared", Skew(Unknown)[appeared.Item1]);
        Assert.Equal("cleared", Skew(Versioned)[appeared.Item2]);
    }

    // Skew is counted over a whole cycle of the directory cursor, so with more due scopes than batchSize, a kind whose
    // skewed item sits on one page is not reported cleared by the other pages (ISK-0009 T068, Version skew rule 2,
    // Diagnostics rule 1).
    [Fact]
    public async Task SkewSpanningSeveralDirectoryPagesIsReportedOnceAndNeverCleared()
    {
        work.RequirePostgreSql();
        var paged = new WorkKind("applies", "skew_paged");
        var filler = new WorkKind("applies", "skew_filler");
        var item = await EnqueueAsync(WorkFixture.FreshOrganization(), paged, 1, "paged");
        foreach (var index in Enumerable.Range(0, 4))
        {
            await EnqueueAsync(WorkFixture.FreshOrganization(), filler, 1, $"filler-scope-{index}");
        }

        var due = await work.ScalarAsync<long>("SELECT count(*) FROM isk_directory.scopes WHERE module = 'work' AND due_at <= clock_timestamp()");
        Dictionary<string, long> skewed;
        await using (var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Handled)], WorkTestSettings.Fast + ",\"batchSize\":2"))
        {
            // Three cycles of the cursor at least, each of more than one page.
            foreach (var pass in Enumerable.Range(0, (int)(3 * ((due / 2) + 1))))
            {
                await a.PassAsync();
            }

            (skewed, _) = await PassAndObserveAsync(a);
        }

        Assert.True(due > 2, "The due scopes fit in one page.");
        Assert.Equal("pending|0", await work.ItemAsync(item));
        Assert.Equal(1L, skewed.GetValueOrDefault(paged.ToString()));
        Assert.Equal(4L, skewed.GetValueOrDefault(filler.ToString()));
        Assert.Equal(["appeared"], Skew(paged));
        Assert.Equal(["appeared"], Skew(filler));
    }

    // A pass that ends the directory cycle with a scope failure still reports the cycle's skew and resets its totals, so
    // failed passes do not add up their counts into the next cycle (ISK-0009 T081, Version skew rule 2, Diagnostics rule 1).
    [Fact]
    public async Task AScopeFailureOnTheCyclesLastPageStillReportsAndResetsTheSkew()
    {
        work.RequirePostgreSql();
        var failing = new WorkKind("applies", "skew_failing");
        var served = new WorkKind("applies", "skew_failing_served");
        var scope = WorkFixture.FreshOrganization();
        var item = await EnqueueAsync(scope, failing, 1, "failing-skewed");
        var handled = await EnqueueAsync(scope, served, 1, "failing-handled");
        await using var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration(served)], WorkTestSettings.Fast);

        // Every shard lease acquire fails, so the scope fails after its counts, on every pass, each a whole cycle.
        a.Leases.FailAcquire = _ => new GatewayException(ErrorCodes.PersistenceUnavailable);
        foreach (var pass in Enumerable.Range(0, 3))
        {
            await Assert.ThrowsAsync<GatewayException>(a.PassAsync);
        }

        var (afterFailures, _) = Observe();
        a.Leases.FailAcquire = null;
        var (afterSuccess, _) = await PassAndObserveAsync(a);

        Assert.Equal(1L, afterFailures.GetValueOrDefault(failing.ToString()));
        Assert.Equal(1L, afterSuccess.GetValueOrDefault(failing.ToString()));
        Assert.Equal(["appeared"], Skew(failing));
        Assert.Equal("pending|0", await work.ItemAsync(item));
        Assert.Equal("completed|1", await work.ItemAsync(handled));
    }

    // The backlog is counted over a whole cycle of the directory cursor too, so with more due scopes than batchSize the
    // gauge is the total over every page after each cycle, not one page's count. Every shard lease acquire fails, so the
    // handled items stay pending and every scope with one fails after its counts (ISK-0009 T089, Diagnostics rule 3, T081).
    [Fact]
    public async Task TheBacklogIsTheTotalOverEveryPageOfTheCycle()
    {
        work.RequirePostgreSql();
        var waiting = new WorkKind("applies", "skew_backlog");
        foreach (var index in Enumerable.Range(0, 5))
        {
            await EnqueueAsync(WorkFixture.FreshOrganization(), waiting, 1, $"backlog-scope-{index}");
        }

        var due = await work.ScalarAsync<long>("SELECT count(*) FROM isk_directory.scopes WHERE module = 'work' AND due_at <= clock_timestamp()");
        var pages = (int)((due / 2) + 1);
        var observed = new List<long>();
        await using (var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration(waiting)], WorkTestSettings.Fast + ",\"batchSize\":2"))
        {
            a.Leases.FailAcquire = _ => new GatewayException(ErrorCodes.PersistenceUnavailable);
            foreach (var pass in Enumerable.Range(0, 3 * pages))
            {
                try
                {
                    await a.PassAsync();
                }
                catch (GatewayException)
                {
                    // A page with one of this test's scopes fails after its counts.
                }

                if (pass >= pages)
                {
                    observed.Add(Observe().Backlog.GetValueOrDefault(waiting.ToString()));
                }
            }
        }

        // Takes the test's scopes out of later tests' directory pages, which some read only once.
        await work.ExecuteAsync(
            "DELETE FROM isk_directory.scopes WHERE module = 'work' AND listed_scope IN (SELECT scope_key FROM isk_work.items WHERE kind = 'applies.skew_backlog')");
        await work.ExecuteAsync("UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE kind = 'applies.skew_backlog'");

        Assert.True(due > 2, "The due scopes fit in one page.");
        Assert.All(observed, value => Assert.Equal(5L, value));
    }

    // The outcomes of this class's 8008 events for kind, in order, from the before'th event of its log.
    private List<string?> Skew(WorkKind kind, int before = 0) =>
        [.. work.Logger.Events.Skip(before)
            .Where(logged => logged.Id.Id == 8008 && logged.Fields.Any(field => field.Key == "Kind" && (string?)field.Value == kind.ToString()))
            .Select(logged => logged.Fields.Single(field => field.Key == "Outcome").Value?.ToString())];

    // Runs one pass, then reads the dispatcher's own gauges, by kind, before it is disposed.
    private static async Task<(Dictionary<string, long> Skewed, Dictionary<string, long> Backlog)> PassAndObserveAsync(TestDispatcher dispatcher)
    {
        await dispatcher.PassAsync();
        return Observe();
    }

    // Reads the live dispatchers' gauges, by kind.
    private static (Dictionary<string, long> Skewed, Dictionary<string, long> Backlog) Observe()
    {
        var measured = new ConcurrentBag<(string Instrument, string Kind, long Value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meters) =>
            {
                if (instrument.Meter.Name == WorkTelemetry.SourceName && instrument.Name is "iskender.work.items.skewed" or "iskender.work.backlog")
                {
                    meters.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == WorkTelemetry.KindLabel && tag.Value is string kind && kind.StartsWith("applies.skew_", StringComparison.Ordinal))
                {
                    measured.Add((instrument.Name, kind, value));
                }
            }
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return (Sum(measured, "iskender.work.items.skewed"), Sum(measured, "iskender.work.backlog"));
    }

    // Other tests' dispatchers in this process report their own kinds, never this class's, so each kind has one value.
    private static Dictionary<string, long> Sum(IEnumerable<(string Instrument, string Kind, long Value)> measured, string instrument) =>
        measured.Where(entry => entry.Instrument == instrument).GroupBy(entry => entry.Kind).ToDictionary(group => group.Key, group => group.Max(entry => entry.Value), StringComparer.Ordinal);

    private async Task<Guid> EnqueueAsync(UnitOfWorkScope scope, WorkKind kind, int version, string key)
    {
        var outbox = new PostgreSqlWorkOutbox([new WorkKindDeclaration(kind, version)], logger: null);
        await using var unit = await work.OpenAsync(scope);
        var item = (await outbox.EnqueueAsync(unit, new WorkItemRequest(scope.Owner, kind, version, new byte[] { 1 }, key), Token)).ItemId;
        await unit.CommitAsync(Token);
        return item;
    }
}
