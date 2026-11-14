using System.Collections.Concurrent;
using System.Globalization;
using Iskender.Core.Contracts.Context;
using Iskender.Hosting.Requests;
using Iskender.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>User Story 4, acceptance scenario 3: concurrent requests each get, and see only, their own context.</summary>
public class LifetimesRequestIsolationTests
{
    private const int Requests = 100;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OneHundredConcurrentRequestsEachSeeOnlyTheirOwnContext()
    {
        var contexts = new ConcurrentBag<GatewayExecutionContext>();
        var arrived = 0;
        var everyoneArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await EmbeddedHost.CreateAsync(
            builder => builder.Services.AddIskender(_ => { }),
            application => application.MapIskender("/iskender").MapGet("/isolation", async (HttpContext http) =>
            {
                // Minimal APIs bind service parameters before the group's filters run, so the context is resolved here.
                var context = http.RequestServices.GetRequiredService<GatewayExecutionContext>();
                contexts.Add(context);
                if (Interlocked.Increment(ref arrived) == Requests)
                {
                    everyoneArrived.SetResult();
                }

                // Every request is in flight at once before any reads its context again.
                await everyoneArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), http.RequestAborted);
                var again = http.RequestServices.GetRequiredService<GatewayExecutionContext>();
                var own = ReferenceEquals(context, again) && context.Correlation.CallerValue == http.Request.Headers[RequestBootstrap.CorrelationHeader].ToString();
                return $"{context.Correlation.RequestId.Value}|{own}";
            }),
            ct: Token);
        var client = host.Client;

        var answers = await Task.WhenAll(Enumerable.Range(0, Requests).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/iskender/isolation");
            request.Headers.Add(RequestBootstrap.CorrelationHeader, "caller-" + index.ToString(CultureInfo.InvariantCulture));
            using var response = await client.SendAsync(request, Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(Token);
        }));

        Assert.Equal(Requests, contexts.Count);
        Assert.Equal(Requests, contexts.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(Requests, answers.Select(answer => answer.Split('|')[0]).Distinct(StringComparer.Ordinal).Count());
        Assert.All(answers, answer => Assert.EndsWith("|True", answer, StringComparison.Ordinal));
        Assert.Equal(Requests, contexts.Select(context => context.Correlation.CallerValue).Distinct(StringComparer.Ordinal).Count());
    }
}
