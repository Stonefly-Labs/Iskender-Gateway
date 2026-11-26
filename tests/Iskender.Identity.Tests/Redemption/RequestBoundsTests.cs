using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Gate;

namespace Iskender.Identity.Tests.Redemption;

/// <summary>
/// The redemption body check (ISK-0010 T027, research R8, US1.2, spec Edge Cases): a body that is over 4 KiB, not
/// <c>application/json</c>, not JSON, or not exactly <c>{"token":"&lt;string&gt;"}</c> gets the uniform <c>403</c> with
/// no read and no record, counted as <c>rejected_body</c>; with an unreachable store, a malformed body or token still gets
/// the uniform <c>403</c>, and a well-formed one <c>503</c> <c>operator.state_unavailable</c>, on both routes.
/// </summary>
public class RequestBoundsTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
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

    public static TheoryData<string> Bodies() =>
        ["oversize", "oversize-unannounced", "text-plain", "no-content-type", "malformed-json", "extra-property", "duplicate-property", "non-string", "array"];

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task ARejectedBodyGetsTheUniformFailureWithNoReadAndNoRecord(string body)
    {
        operators.RequirePostgreSql();
        var prefix = "/bounds-" + body;
        using var denials = new DenialCapture(prefix);
        var token = (await new Infrastructure.PostgreSql.Identity.PostgreSqlOperatorTokens(
                operators.Factory, new Infrastructure.PostgreSql.Audit.PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
            .IssueAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Context(OperatorActor.OperatorCommand), Token)).Reveal();
        var before = await operators.AllRowsAsync();
        await using var host = await operators.HostAsync(new OperatorHostOptions { Prefix = prefix });
        await using var unreachable = await operators.HostAsync(new OperatorHostOptions { Prefix = prefix, Unreachable = true });

        foreach (var route in new[] { "bootstrap", "recovery" })
        {
            using var reachable = await host.Client.PostAsync($"{prefix}/{route}", Content(body, token), Token);
            using var unread = await unreachable.Client.PostAsync($"{prefix}/{route}", Content(body, token), Token);

            await AdminRouteTests.AssertErrorAsync(reachable, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
            await AdminRouteTests.AssertErrorAsync(unread, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
        }

        Assert.Equal(before, await operators.AllRowsAsync());
        Assert.Equal(
            [$"{prefix}/bootstrap rejected_body", $"{prefix}/recovery rejected_body"],
            denials.Denials.Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(4, denials.Denials.Count);
    }

    [Fact]
    public async Task WithAnUnreachableStoreAMalformedTokenFailsAndAWellFormedOneIs503()
    {
        operators.RequirePostgreSql();
        await using var host = await operators.HostAsync(new OperatorHostOptions { Unreachable = true });

        foreach (var purpose in new[] { OperatorTokenPurpose.Bootstrap, OperatorTokenPurpose.Recovery })
        {
            var path = $"/admin/v1/{purpose.ToCode()}";
            using var malformed = await BootstrapRedemptionTests.RedeemAsync(host, path, "isk" + purpose.ToPrefixLetter() + "_not-a-token");
            using var wellFormed = await BootstrapRedemptionTests.RedeemAsync(host, path, new IssuedOperatorToken(OperatorTokenText.Generate(purpose), DateTimeOffset.UtcNow).Reveal());

            await AdminRouteTests.AssertErrorAsync(malformed, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
            await AdminRouteTests.AssertErrorAsync(wellFormed, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorStateUnavailable);
        }
    }

    // A body of each rejected kind; every one but the JSON-shape cases holds the stored token itself.
    private static HttpContent Content(string kind, string token) => kind switch
    {
        "oversize" => Json($"{{\"token\":\"{token}{new string(' ', 4096)}\"}}"),
        // Exactly {"token":"<string>"}, so only the read bound rejects it as a body: without the bound, the shape check
        // would pass and the token's parse fail, which records no rejected_body denial (FR-012; CONV-0010-013).
        "oversize-unannounced" => new UnannouncedContent(Encoding.UTF8.GetBytes($"{{\"token\":\"{token}{new string('x', 4096)}\"}}")),
        "text-plain" => new StringContent($"{{\"token\":\"{token}\"}}", Encoding.UTF8, "text/plain"),
        "no-content-type" => new ByteArrayContent(Encoding.UTF8.GetBytes($"{{\"token\":\"{token}\"}}")),
        "malformed-json" => Json($"{{\"token\":\"{token}\""),
        "extra-property" => Json($"{{\"token\":\"{token}\",\"other\":1}}"),
        "duplicate-property" => Json($"{{\"token\":\"{token}\",\"token\":\"{token}\"}}"),
        "non-string" => Json("{\"token\":42}"),
        _ => Json($"[\"{token}\"]"),
    };

    private static StringContent Json(string text) => new(text, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

    // A body that does not announce its length, so the size bound is enforced while reading it.
    private sealed class UnannouncedContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnannouncedContent(byte[] bytes)
        {
            _bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
