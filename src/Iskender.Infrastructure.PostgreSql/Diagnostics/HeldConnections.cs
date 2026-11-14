using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Iskender.Infrastructure.PostgreSql.Diagnostics;

/// <summary>
/// The connections held by open units of work on one <see cref="PostgreSqlDataSources"/>, by module, reported through its
/// own <c>iskender.persistence.pool.connections</c> gauge (ISK-0006 research R11). Each data sources instance owns one and
/// disposes it, which stops its gauge, so two containers in one process never share a count. Owned by ISK-0006.
/// </summary>
internal sealed class HeldConnections : IDisposable
{
    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);
    private readonly Meter _meter = new(PostgreSqlAdapter.DiagnosticsName);

    public HeldConnections()
    {
        Gauge = _meter.CreateObservableGauge(
            "iskender.persistence.pool.connections",
            () => _counts.Select(entry => new Measurement<long>(entry.Value, new KeyValuePair<string, object?>(PersistenceTelemetry.ModuleTag, entry.Key))),
            "{connection}",
            "Connections held by open units of work.");
    }

    /// <summary>This instance's gauge, so a test can tell it from another instance's gauge of the same name.</summary>
    internal ObservableGauge<long> Gauge { get; }

    /// <summary>The connections <paramref name="module"/>'s open units of work hold now.</summary>
    public long CountOf(string module) => _counts.GetValueOrDefault(module);

    /// <summary>The connections every module's open units of work hold now.</summary>
    public long Total => _counts.Values.Sum();

    public void Held(string module) => _counts.AddOrUpdate(module, 1, (_, count) => count + 1);

    public void Released(string module) => _counts.AddOrUpdate(module, 0, (_, count) => Math.Max(0, count - 1));

    public void Dispose() => _meter.Dispose();
}
