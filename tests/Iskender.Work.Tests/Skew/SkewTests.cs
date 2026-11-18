using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
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
        Assert.Equal(["appeared"], Skew(Unknown));
        Assert.Equal(["appeared"], Skew(Versioned));
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

        await using (var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration(Handled)], WorkTestSettings.Fast + ",\"batchSize\":2"))
        {
            await a.PassAsync();
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

    // The outcomes of this class's 8008 events for kind, in order.
    private List<string?> Skew(WorkKind kind) =>
        [.. work.Logger.Events
            .Where(logged => logged.Id.Id == 8008 && logged.Fields.Any(field => field.Key == "Kind" && (string?)field.Value == kind.ToString()))
            .Select(logged => logged.Fields.Single(field => field.Key == "Outcome").Value?.ToString())];

    // Runs one pass, then reads the dispatcher's own gauges, by kind, before it is disposed.
    private static async Task<(Dictionary<string, long> Skewed, Dictionary<string, long> Backlog)> PassAndObserveAsync(TestDispatcher dispatcher)
    {
        await dispatcher.PassAsync();
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
