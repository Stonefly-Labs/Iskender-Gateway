using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Audit;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Redemption;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Management.Security;

namespace Iskender.Identity.Tests.Canary;

/// <summary>
/// The telemetry canary (ISK-0010 T052, US2.6, SC-004, FR-007, FR-038, operator-security contract Test fixtures rule 3):
/// issue, supersede, every redemption outcome, recovery, enrollment consumption, every gate, authentication, and
/// privilege denial, and the limit run with capturing loggers, an activity listener, and a meter listener; then every
/// captured log, span, tag, metric label, error body, audit record, and database row is searched for each token's and
/// code's secret and full text, and for each hash outside <c>isk_identity.tokens</c>, and the logs, spans, and metrics
/// for each principal ID. None is found. The embedded host has no health listener, so no readiness answer is captured;
/// the container's readiness bodies hold no installation state (T037).
/// </summary>
[Collection(RedemptionCounting.Name)]
public class TelemetryCanaryTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private const string Prefix = "/admin/v1";

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
    public async Task NoSecretHashOrPrincipalIdReachesAnyCapturedOutput()
    {
        operators.RequirePostgreSql();
        using var telemetry = new Capture();
        var lifetimes = OperatorFixture.Configuration().Lifetimes;
        var tokens = new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), lifetimes);
        var texts = new List<string>();
        var principals = new List<string>();
        var errors = new List<string>();
        await using var host = await operators.HostAsync();
        await using var limitedHost = await operators.HostAsync(new OperatorHostOptions { OperatorFields = "\"bootstrap\":{\"attemptsPerMinute\":1}" });

        async Task<string> IssueAsync(OperatorTokenPurpose purpose)
        {
            var text = (await tokens.IssueAsync(purpose, OperatorFixture.Context(OperatorActor.OperatorCommand), Token)).Reveal();
            texts.Add(text);
            return text;
        }

        async Task<HttpResponseMessage> RedeemAsync(OperatorHost on, string route, string text)
        {
            var response = await BootstrapRedemptionTests.RedeemAsync(on, $"{Prefix}/{route}", text);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                errors.Add(await response.Content.ReadAsStringAsync(Token));
            }

            return response;
        }

        async Task GetAsync(string? principal)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Prefix}/installation");
            if (principal is not null)
            {
                request.Headers.Add(TestOperatorPrincipalResolver.Header, principal);
            }

            using var response = await host.Client.SendAsync(request, Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                errors.Add(await response.Content.ReadAsStringAsync(Token));
            }
        }

        var ungranted = (await operators.PrincipalAsync(granted: false)).Value;
        principals.Add(ungranted);

        // Before the first operator: the gate, an unknown identifier, a malformed token, and a malformed body.
        await GetAsync(ungranted);
        (await RedeemAsync(host, "bootstrap", new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow).Reveal())).Dispose();
        (await RedeemAsync(host, "bootstrap", "iskb_not-a-token")).Dispose();

        // Issue and supersede, then wrong secret, superseded, expired, wrong purpose, and not initialized.
        var superseded = await IssueAsync(OperatorTokenPurpose.Bootstrap);
        var expired = await IssueAsync(OperatorTokenPurpose.Bootstrap);
        (await RedeemAsync(host, "bootstrap", OperatorFixture.WithOtherSecret(expired))).Dispose();
        (await RedeemAsync(host, "bootstrap", superseded)).Dispose();
        await operators.ExpireAsync(OperatorFixture.Parse(expired).TokenId);
        (await RedeemAsync(host, "bootstrap", expired)).Dispose();
        var current = await IssueAsync(OperatorTokenPurpose.Bootstrap);
        (await RedeemAsync(host, "recovery", current)).Dispose();

        // Success, consumed, and the limit.
        using (var redeemed = await RedeemAsync(host, "bootstrap", current))
        {
            using var body = JsonDocument.Parse(await redeemed.Content.ReadAsStringAsync(Token));
            principals.Add(body.RootElement.GetProperty("principalId").GetString()!);
            texts.Add(body.RootElement.GetProperty("enrollmentCode").GetString()!);
        }

        (await RedeemAsync(host, "bootstrap", current)).Dispose();
        (await RedeemAsync(limitedHost, "bootstrap", current)).Dispose();
        (await RedeemAsync(limitedHost, "bootstrap", current)).Dispose();

        // A second bootstrap token on an initialized installation, recovery, and an enrollment consumption.
        await operators.ExecuteAsync("DELETE FROM isk_identity.installation");
        var late = await IssueAsync(OperatorTokenPurpose.Bootstrap);
        await operators.InitializeInstallationAsync();
        (await RedeemAsync(host, "bootstrap", late)).Dispose();
        var recovery = await IssueAsync(OperatorTokenPurpose.Recovery);
        using (var recovered = await RedeemAsync(host, "recovery", recovery))
        {
            using var body = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync(Token));
            var principal = body.RootElement.GetProperty("principalId").GetString()!;
            principals.Add(principal);
            var code = body.RootElement.GetProperty("enrollmentCode").GetString()!;
            texts.Add(code);
            await using var unit = await operators.OpenAsync();
            var parsed = ResourceId.TryParse(principal, ResourceTypes.Principal, out var id, out _);
            Assert.True(parsed);
            await new PostgreSqlEnrollmentCodes(new PostgreSqlAuditRecorder())
                .ConsumeAsync(unit, id!, OperatorFixture.Parse(code), OperatorFixture.Context(OperatorActor.ForPrincipal(id!)), Token);
            await unit.CommitAsync(Token);
        }

        // Authentication and privilege denials, and the privileged read.
        await GetAsync(null);
        await GetAsync(ungranted);
        await GetAsync(principals[1]);

        var logs = string.Join('\n', host.Host.Logs.Entries.Concat(limitedHost.Host.Logs.Entries)
            .Select(entry => entry.Message + " " + string.Join(' ', entry.Values.Select(value => $"{value.Key}={value.Value}"))));
        var observed = logs + "\n" + telemetry.Text;
        var rows = await operators.ScalarAsync<string>(
            "SELECT concat_ws(E'\\n', (SELECT json_agg(t)::text FROM isk_identity.installation t), (SELECT json_agg(t)::text FROM isk_identity.principals t), "
            + "(SELECT json_agg(t)::text FROM isk_identity.operator_grants t), (SELECT json_agg(t)::text FROM isk_audit.records t), "
            + "(SELECT json_agg(json_build_object('id', token_id, 'purpose', purpose, 'principal', principal_id))::text FROM isk_identity.tokens))");
        var everything = observed + "\n" + string.Join('\n', errors) + "\n" + rows;
        var hashes = (await operators.ScalarAsync<string>("SELECT string_agg(encode(secret_hash, 'hex') || ',' || encode(secret_hash, 'base64'), ',') FROM isk_identity.tokens"))
            .Split(',').Select(hash => hash.TrimEnd('=')).ToList();

        // The capture saw the run: redemption and authorization spans, failed and successful redemption events, and the metrics.
        Assert.True(texts.Count >= 7);
        Assert.Contains(OperatorTelemetry.RedeemActivity, telemetry.Text, StringComparison.Ordinal);
        Assert.Contains(OperatorTelemetry.AuthorizeActivity, telemetry.Text, StringComparison.Ordinal);
        Assert.NotEmpty(host.Host.Logs.ById(9001));
        Assert.True(host.Host.Logs.ById(9002).Count >= 8);
        Assert.Contains(telemetry.Metrics, label => label.Contains("reason=wrong_secret", StringComparison.Ordinal));
        Assert.Contains(telemetry.Metrics, label => label.Contains("reason=limited", StringComparison.Ordinal));
        Assert.Contains(telemetry.Metrics, label => label.Contains("reason=forbidden", StringComparison.Ordinal));
        foreach (var text in texts)
        {
            Assert.DoesNotContain(text, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(OperatorFixture.Secret(text), everything, StringComparison.Ordinal);
        }

        Assert.All(hashes, hash => Assert.DoesNotContain(hash, everything, StringComparison.OrdinalIgnoreCase));
        Assert.All(principals, principal => Assert.DoesNotContain(principal, observed, StringComparison.Ordinal));
    }

    // Every Iskender span, with its name, tags, events, and status, and every operator metric measurement's labels.
    private sealed class Capture : IDisposable
    {
        private readonly ActivityListener _activities;
        private readonly MeterListener _meters;
        private readonly ConcurrentQueue<string> _spans = new();
        private readonly ConcurrentQueue<string> _metrics = new();

        public Capture()
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Iskender", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _spans.Enqueue(Describe(activity)),
            };
            ActivitySource.AddActivityListener(_activities);
            _meters = new MeterListener
            {
                InstrumentPublished = (instrument, meters) =>
                {
                    if (instrument.Meter.Name == OperatorTelemetry.SourceName)
                    {
                        meters.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _meters.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            {
                var labels = new StringBuilder(instrument.Name);
                foreach (var tag in tags)
                {
                    labels.Append(' ').Append(tag.Key).Append('=').Append(tag.Value);
                }

                _metrics.Enqueue(labels.ToString());
            });
            _meters.Start();
        }

        public IReadOnlyList<string> Metrics => [.. _metrics];

        public string Text => string.Join('\n', _spans.Concat(_metrics));

        public void Dispose()
        {
            _activities.Dispose();
            _meters.Dispose();
        }

        private static string Describe(Activity activity)
        {
            var text = new StringBuilder(activity.Source.Name).Append(' ').Append(activity.DisplayName).Append(' ').Append(activity.StatusDescription);
            foreach (var tag in activity.TagObjects)
            {
                text.Append(' ').Append(tag.Key).Append('=').Append(tag.Value);
            }

            foreach (var activityEvent in activity.Events)
            {
                text.Append(' ').Append(activityEvent.Name);
                foreach (var tag in activityEvent.Tags)
                {
                    text.Append(' ').Append(tag.Key).Append('=').Append(tag.Value);
                }
            }

            return text.ToString();
        }
    }
}
