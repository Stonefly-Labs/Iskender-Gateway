using System.Diagnostics;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Hosting.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Requests;

/// <summary>
/// The first endpoint filter of every Iskender route group: creates the request's correlation context and
/// <see cref="GatewayExecutionContext"/>, resolves its configuration revision, and stores it in the request's
/// <see cref="RequestContextHolder"/> (hosting contract, Route group rules 2 to 4; research R14). Owned by ISK-0006.
/// </summary>
internal sealed class RequestBootstrap : IEndpointFilter
{
    /// <summary>The request header a caller's correlation value is read from.</summary>
    public const string CorrelationHeader = "X-Correlation-ID";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var snapshot = services.GetRequiredService<ConfigurationSnapshot>();
        var execution = Create(
            http,
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetRequiredService<HostingSettings>().RequestTimeout,
            snapshot.Revision);
        services.GetRequiredService<RequestContextHolder>().Context = execution;
        return next(context);
    }

    /// <summary>
    /// The request's context: a new gateway ID, the caller's value if it has the bounded form, the current activity's IDs
    /// or new random ones, a deadline of now plus <paramref name="timeout"/>, and the request's <c>RequestAborted</c>.
    /// </summary>
    internal static GatewayExecutionContext Create(HttpContext http, TimeProvider time, TimeSpan timeout, ConfigurationRevision revision)
    {
        // Several header values join with commas, which the bounded form excludes, so they are dropped together.
        var caller = http.Request.Headers.TryGetValue(CorrelationHeader, out var values) ? values.ToString() : null;
        var activity = Activity.Current;
        var correlation = new CorrelationContext(
            RequestCorrelationId.New(),
            activity?.TraceId ?? ActivityTraceId.CreateRandom(),
            activity?.SpanId ?? ActivitySpanId.CreateRandom(),
            caller);
        var execution = new GatewayExecutionContext(correlation, time.GetUtcNow() + timeout, time, http.RequestAborted);
        execution.ResolveConfigurationRevision(revision);
        return execution;
    }
}
