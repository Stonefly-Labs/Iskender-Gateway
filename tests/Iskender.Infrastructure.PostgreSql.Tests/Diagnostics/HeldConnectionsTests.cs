using System.Diagnostics.Metrics;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Diagnostics;

/// <summary>Each data sources instance counts and reports only its own held connections (ISK-0006 research R11).</summary>
public class HeldConnectionsTests(EmptyDatabase database) : IClassFixture<EmptyDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoDataSourcesReportOnlyTheirOwnCountsAndADisposedOneStopsItsGauge()
    {
        database.RequirePostgreSql();
        await using var first = TestStore.For(database);
        var second = TestStore.For(database);
        var ours = new[] { first.Sources.HeldConnections.Gauge, second.Sources.HeldConnections.Gauge };
        var measured = new List<(Instrument Gauge, string Module, long Value)>();
        var completed = new List<Instrument>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, listening) =>
            {
                if (ours.Contains(instrument))
                {
                    listening.EnableMeasurementEvents(instrument);
                }
            },
            MeasurementsCompleted = (instrument, _) => completed.Add(instrument),
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measured.Add((instrument, (string)tags[0].Value!, value)));
        listener.Start();

        await using (await first.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions("alpha", "test.held")))
        await using (await first.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions("alpha", "test.held")))
        await using (await second.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions("beta", "test.held")))
        {
            listener.RecordObservableInstruments();

            Assert.Equal(2, first.Sources.HeldConnections.CountOf("alpha"));
            Assert.Equal(0, first.Sources.HeldConnections.CountOf("beta"));
            Assert.Equal(1, second.Sources.HeldConnections.CountOf("beta"));
            Assert.Equal([(ours[0], "alpha", 2L)], measured.Where(entry => entry.Gauge == ours[0]));
            Assert.Equal([(ours[1], "beta", 1L)], measured.Where(entry => entry.Gauge == ours[1]));
        }

        Assert.Equal(0, first.Sources.HeldConnections.Total);
        Assert.Equal(0, second.Sources.HeldConnections.Total);

        await second.DisposeAsync();
        measured.Clear();
        listener.RecordObservableInstruments();

        Assert.Equal([ours[1]], completed);
        Assert.DoesNotContain(measured, entry => entry.Gauge == ours[1]);
        Assert.Contains(measured, entry => entry.Gauge == ours[0]);
    }
}
