using System.Diagnostics;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Requests;

/// <summary>The request bootstrap (hosting contract, Route group rules 2 to 4; research R14).</summary>
public class RequestBootstrapTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, bool> CallerValues => new()
    {
        { "a", true },
        { new string('x', 128), true },
        { "Az09._:-", true },
        { new string('x', 129), false },
        { string.Empty, false },
        { "has space", false },
        { "comma,separated", false },
        { "é", false },
        { "slash/", false },
    };

    [Theory]
    [MemberData(nameof(CallerValues))]
    public async Task ACallerValueIsKeptOnlyInTheBoundedForm(string value, bool kept)
    {
        var context = await BootstrapAsync(http => http.Request.Headers[RequestBootstrap.CorrelationHeader] = value);

        Assert.Equal(kept ? value : null, context.Correlation.CallerValue);
        Assert.Equal(!kept, context.Correlation.CallerValueDropped);
        Assert.StartsWith("req_", context.Correlation.RequestId.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeveralCallerValuesAreDropped()
    {
        var context = await BootstrapAsync(http => http.Request.Headers[RequestBootstrap.CorrelationHeader] = new(["one", "two"]));

        Assert.Null(context.Correlation.CallerValue);
        Assert.True(context.Correlation.CallerValueDropped);
    }

    [Fact]
    public async Task WithNoCallerValueNothingIsKeptOrDropped()
    {
        var context = await BootstrapAsync();

        Assert.Null(context.Correlation.CallerValue);
        Assert.False(context.Correlation.CallerValueDropped);
    }

    [Fact]
    public async Task EachRequestGetsItsOwnGatewayId()
    {
        var first = await BootstrapAsync();
        var second = await BootstrapAsync();

        Assert.NotEqual(first.Correlation.RequestId, second.Correlation.RequestId);
    }

    [Fact]
    public async Task TheDeadlineIsTheStartPlusTheConfiguredTimeoutFromTheContainersTimeProvider()
    {
        var time = new FixedTime(Now);

        var context = await BootstrapAsync(time: time, timeout: "20s");

        Assert.Equal(Now.AddSeconds(20), context.Deadline);
        Assert.False(context.IsExpired);
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.True(context.IsExpired);
    }

    [Fact]
    public async Task TheCancellationIsTheRequestsAbortedToken()
    {
        using var aborted = new CancellationTokenSource();

        var context = await BootstrapAsync(http => http.RequestAborted = aborted.Token);

        Assert.Equal(aborted.Token, context.CancellationToken);
    }

    [Fact]
    public async Task TheRevisionIsTheSnapshots()
    {
        ConfigurationRevision? revision = null;

        var context = await BootstrapAsync(services: provider => revision = provider.GetRequiredService<ConfigurationSnapshot>().Revision);

        Assert.Equal(revision, context.ConfigurationRevision);
    }

    [Fact]
    public async Task TheTraceIdsAreTheCurrentActivitysWhenThereIsOne()
    {
        using var activity = new Activity("host-request").SetIdFormat(ActivityIdFormat.W3C).Start();

        var context = await BootstrapAsync();

        Assert.Equal(activity.TraceId, context.Correlation.TraceId);
        Assert.Equal(activity.SpanId, context.Correlation.SpanId);
    }

    [Fact]
    public async Task WithNoCurrentActivityTheTraceIdsAreNewAndRandom()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var first = await BootstrapAsync();
            var second = await BootstrapAsync();

            Assert.NotEqual(default, first.Correlation.TraceId);
            Assert.NotEqual(default, first.Correlation.SpanId);
            Assert.NotEqual(first.Correlation.TraceId, second.Correlation.TraceId);
            Assert.NotEqual(first.Correlation.SpanId, second.Correlation.SpanId);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public async Task OutsideARequestTheContextIsNotResolved()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();

        var failure = Assert.Throws<GatewayException>(() => scope.ServiceProvider.GetRequiredService<GatewayExecutionContext>());

        Assert.Same(ErrorCodes.ContextNotResolved, failure.Code);
    }

    [Fact]
    public async Task TheHolderIsSetOnce()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var holder = scope.ServiceProvider.GetRequiredService<RequestContextHolder>();
        var context = RequestBootstrap.Create(http, TimeProvider.System, TimeSpan.FromSeconds(1), provider.GetRequiredService<ConfigurationRevision>());
        holder.Context = context;

        var failure = Assert.Throws<GatewayException>(() => holder.Context = context);

        Assert.Same(ErrorCodes.ContextAlreadyResolved, failure.Code);
        Assert.Same(context, scope.ServiceProvider.GetRequiredService<GatewayExecutionContext>());
    }

    [Fact]
    public async Task TheBootstrapSetsTheContextBeforeTheEndpointRuns()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        GatewayExecutionContext? seen = null;

        await new RequestBootstrap().InvokeAsync(EndpointFilterInvocationContext.Create(http), invocation =>
        {
            seen = invocation.HttpContext.RequestServices.GetRequiredService<GatewayExecutionContext>();
            return ValueTask.FromResult<object?>(null);
        });

        Assert.NotNull(seen);
        Assert.Same(seen, scope.ServiceProvider.GetRequiredService<RequestContextHolder>().Context);
    }

    private static ServiceProvider Provider(TimeProvider? time = null, string timeout = "100s")
    {
        var services = new ServiceCollection();
        if (time is not null)
        {
            services.AddSingleton(time);
        }

        services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText(
            "base.json", IskenderDocumentFormat.Json, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"" + timeout + "\"}}}"));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    // Runs the bootstrap for one request in its own scope, and returns the context it stored.
    private static async Task<GatewayExecutionContext> BootstrapAsync(
        Action<HttpContext>? request = null, TimeProvider? time = null, string timeout = "100s", Action<IServiceProvider>? services = null)
    {
        await using var provider = Provider(time, timeout);
        services?.Invoke(provider);
        await using var scope = provider.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        request?.Invoke(http);

        await new RequestBootstrap().InvokeAsync(EndpointFilterInvocationContext.Create(http), _ => ValueTask.FromResult<object?>(null));

        return scope.ServiceProvider.GetRequiredService<RequestContextHolder>().Required;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
