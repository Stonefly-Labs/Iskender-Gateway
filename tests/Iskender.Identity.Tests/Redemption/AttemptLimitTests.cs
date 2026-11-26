using System.Net;
using System.Threading.RateLimiting;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Gate;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Identity.Tests.Redemption;

/// <summary>
/// The redemption attempt limit (ISK-0010 T027, FR-012, research R8): with the default of 10, the 11th attempt in a minute
/// on one host, across both routes together, answers <c>429</c> <c>operator.redemption_limited</c> with no database
/// read, shown by an unreachable-store host answering <c>429</c> rather than <c>503</c>; each host has its own limit; and
/// event 9006 is written at most once a minute; and a host's own <see cref="RateLimiter"/> neither changes the limit nor
/// is replaced (T075, CONV-0010-019). The attempts finish well inside one window.
/// </summary>
public class AttemptLimitTests(OperatorFixture operators) : IClassFixture<OperatorFixture>
{
    private const int DefaultLimit = 10;

    [Fact]
    public async Task TheEleventhAttemptAcrossBothRoutesIsLimitedWithNoRead()
    {
        operators.RequirePostgreSql();
        const string prefix = "/limit-one";
        using var denials = new DenialCapture(prefix);
        await using var host = await operators.HostAsync(new OperatorHostOptions { Unreachable = true, Prefix = prefix });

        for (var attempt = 0; attempt < DefaultLimit; attempt++)
        {
            using var admitted = await RedeemAsync(host, prefix, attempt);
            await AdminRouteTests.AssertErrorAsync(admitted, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorStateUnavailable);
        }

        for (var attempt = DefaultLimit; attempt < DefaultLimit + 3; attempt++)
        {
            using var limited = await RedeemAsync(host, prefix, attempt);
            await AdminRouteTests.AssertErrorAsync(limited, HttpStatusCode.TooManyRequests, ErrorCodes.OperatorRedemptionLimited);
        }

        Assert.Single(host.Host.Logs.ById(9006));
        Assert.Equal(3, denials.Denials.Count(denial => denial.EndsWith(" limited", StringComparison.Ordinal)));
        Assert.Contains($"{prefix}/recovery limited", denials.Denials);
    }

    [Fact]
    public async Task EachHostHasItsOwnLimit()
    {
        operators.RequirePostgreSql();
        const string prefix = "/limit-two";
        await using var first = await operators.HostAsync(new OperatorHostOptions { Unreachable = true, Prefix = prefix });
        await using var second = await operators.HostAsync(new OperatorHostOptions { Unreachable = true, Prefix = prefix });

        for (var attempt = 0; attempt < DefaultLimit; attempt++)
        {
            using var admitted = await RedeemAsync(first, prefix, attempt);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, admitted.StatusCode);
        }

        using var limited = await RedeemAsync(first, prefix, DefaultLimit);
        using var other = await RedeemAsync(second, prefix, DefaultLimit);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await AdminRouteTests.AssertErrorAsync(other, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorStateUnavailable);
    }

    [Fact]
    public async Task AHostRateLimiterNeitherChangesTheLimitNorIsReplaced()
    {
        operators.RequirePostgreSql();
        const string prefix = "/limit-three";
        using var own = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        await using var host = await operators.HostAsync(new OperatorHostOptions
        {
            Unreachable = true,
            Prefix = prefix,
            Services = services => services.AddSingleton<RateLimiter>(own),
        });

        for (var attempt = 0; attempt < DefaultLimit; attempt++)
        {
            using var admitted = await RedeemAsync(host, prefix, attempt);
            await AdminRouteTests.AssertErrorAsync(admitted, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorStateUnavailable);
        }

        using var limited = await RedeemAsync(host, prefix, DefaultLimit);

        await AdminRouteTests.AssertErrorAsync(limited, HttpStatusCode.TooManyRequests, ErrorCodes.OperatorRedemptionLimited);
        Assert.Same(own, host.Services.GetRequiredService<RateLimiter>());
        Assert.Equal(1, own.GetStatistics()!.CurrentAvailablePermits);
    }

    // A well-formed token, alternating between the two routes.
    private static Task<HttpResponseMessage> RedeemAsync(OperatorHost host, string prefix, int attempt)
    {
        var purpose = attempt % 2 == 0 ? OperatorTokenPurpose.Bootstrap : OperatorTokenPurpose.Recovery;
        var token = new IssuedOperatorToken(OperatorTokenText.Generate(purpose), DateTimeOffset.UtcNow).Reveal();
        return BootstrapRedemptionTests.RedeemAsync(host, $"{prefix}/{purpose.ToCode()}", token);
    }
}
