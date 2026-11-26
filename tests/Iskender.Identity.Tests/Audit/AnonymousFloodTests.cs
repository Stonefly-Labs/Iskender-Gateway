using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Redemption;
using Iskender.Management.Security;

namespace Iskender.Identity.Tests.Audit;

/// <summary>Runs <see cref="AnonymousFloodTests"/> alone, since the redemption counter carries no route to filter by.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RedemptionCounting
{
    public const string Name = "Redemption counting";
}

/// <summary>
/// ISK-0010 T043, US5.4, SC-007, operator-security contract Audit records rule 5: 1,000 anonymous denied requests, as
/// uninitialized-gate denials, malformed bodies, malformed tokens, and unknown identifiers spread over two hosts with
/// <c>attemptsPerMinute</c> 600, add no audit record, and <c>iskender.identity.admin.denials</c> and
/// <c>iskender.identity.redemptions</c> count every one.
/// </summary>
[Collection(RedemptionCounting.Name)]
public class AnonymousFloodTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private const string Prefix = "/admin/flood";

    private const int EachKind = 250;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (operators.Postgres.UnavailableReason is null)
        {
            await operators.ResetAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AThousandDeniedRequestsAddNoRecordAndAreEachCounted()
    {
        operators.RequirePostgreSql();
        var options = new OperatorHostOptions { Prefix = Prefix, OperatorFields = "\"bootstrap\":{\"attemptsPerMinute\":600}" };
        await using var first = await operators.HostAsync(options);
        await using var second = await operators.HostAsync(options);
        using var denials = new DenialCapture(Prefix);
        using var redemptions = new RedemptionCapture();
        var before = await operators.AuditCountAsync();

        for (var index = 0; index < EachKind; index++)
        {
            var host = index % 2 == 0 ? first : second;
            using var gate = await host.Client.GetAsync($"{Prefix}/installation", Token);
            using var body = await host.Client.PostAsync($"{Prefix}/bootstrap", new StringContent("not json", Encoding.UTF8, "application/json"), Token);
            using var malformed = await BootstrapRedemptionTests.RedeemAsync(host, $"{Prefix}/bootstrap", "isk_not-a-token");
            using var unknown = await BootstrapRedemptionTests.RedeemAsync(host, $"{Prefix}/recovery", new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Recovery), DateTimeOffset.UtcNow).Reveal());

            Assert.Equal(HttpStatusCode.Forbidden, malformed.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, body.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, gate.StatusCode);
        }

        Assert.Equal(before, await operators.AuditCountAsync());
        Assert.Equal(0L, await operators.AuditCountAsync());
        Assert.Equal(2 * EachKind, denials.Denials.Count);
        Assert.Equal(EachKind, denials.Denials.Count(denial => denial.EndsWith(" rejected_body", StringComparison.Ordinal)));
        Assert.Equal(
            [("bootstrap", "malformed", 2 * EachKind), ("recovery", "unknown", EachKind)],
            redemptions.Counts.OrderBy(count => count.Purpose, StringComparer.Ordinal).ToArray());
    }

    // The redemption counter's measurements while this test runs, by purpose and reason class.
    private sealed class RedemptionCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly ConcurrentQueue<(string Purpose, string Reason)> _measurements = new();

        public RedemptionCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, meters) =>
                {
                    if (instrument.Meter.Name == OperatorTelemetry.SourceName && instrument.Name == "iskender.identity.redemptions")
                    {
                        meters.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? purpose = null, reason = null;
                foreach (var tag in tags)
                {
                    purpose = tag.Key == "purpose" ? tag.Value as string : purpose;
                    reason = tag.Key == "reason" ? tag.Value as string : reason;
                }

                for (var count = 0; count < value; count++)
                {
                    _measurements.Enqueue((purpose ?? "-", reason ?? "-"));
                }
            });
            _listener.Start();
        }

        public IEnumerable<(string Purpose, string Reason, int Count)> Counts =>
            _measurements.GroupBy(measurement => measurement).Select(group => (group.Key.Purpose, group.Key.Reason, group.Count()));

        public void Dispose() => _listener.Dispose();
    }
}
