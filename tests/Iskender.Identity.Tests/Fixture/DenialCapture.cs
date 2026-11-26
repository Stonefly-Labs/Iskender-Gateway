using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Iskender.Management.Security;

namespace Iskender.Identity.Tests.Fixture;

/// <summary>
/// Records the <c>iskender.identity.admin.denials</c> measurements whose route template starts with one test's own prefix,
/// since every host's meter has the same name and test classes run in parallel.
/// </summary>
internal sealed class DenialCapture : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentQueue<string> _reasons = new();

    public DenialCapture(string prefix)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, meters) =>
            {
                if (instrument.Meter.Name == OperatorTelemetry.SourceName && instrument.Name == "iskender.identity.admin.denials")
                {
                    meters.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? route = null, reason = null;
            foreach (var tag in tags)
            {
                route = tag.Key == "route" ? tag.Value as string : route;
                reason = tag.Key == "reason" ? tag.Value as string : reason;
            }

            if (route?.StartsWith(prefix + "/", StringComparison.Ordinal) == true)
            {
                for (var count = 0; count < value; count++)
                {
                    _reasons.Enqueue($"{route} {reason}");
                }
            }
        });
        _listener.Start();
    }

    /// <summary>Each denial, as <c>&lt;route&gt; &lt;reason&gt;</c>, in the order recorded.</summary>
    public IReadOnlyList<string> Denials => [.. _reasons];

    public void Dispose() => _listener.Dispose();
}
