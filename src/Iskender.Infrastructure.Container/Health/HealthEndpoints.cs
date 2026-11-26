using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.Container.Health;

/// <summary>
/// The health listener's branch: <c>GET /health/live</c> and <c>GET /health/ready</c>, and <c>404</c> with no body for
/// anything else (container contract, Listeners rule 3, Liveness, and Readiness rules 1 and 10; research R10). Owned by
/// ISK-0007.
/// </summary>
internal static class HealthEndpoints
{
    public const string LivePath = "/health/live";

    public const string ReadyPath = "/health/ready";

    private const string JsonType = "application/json";

    // A constant, so the health listener keeps no static state (hosting contract, Static state). It is ASCII.
    private const string Live = """{"version":1,"status":"live"}""";

    /// <summary>
    /// The health listener's routes, as <c>(method, route)</c>, projected from the table <see cref="HandleAsync"/> serves
    /// from, so the list and the handler cannot drift (research R19, ISK-0010). A computed property, so no static field
    /// holds it.
    /// </summary>
    public static ImmutableArray<(string Method, string Route)> Routes => [.. Handlers.Select(entry => (entry.Method, entry.Route))];

    // The one table of what the health listener serves.
    private static ImmutableArray<(string Method, string Route, Func<HttpContext, Task> Handle)> Handlers =>
    [
        (HttpMethods.Get, LivePath, LiveAsync),
        (HttpMethods.Get, ReadyPath, ReadyAsync),
    ];

    /// <summary>
    /// Answers one request on the health listener: the entry of <see cref="Routes"/> that matches its method and path, or
    /// <c>404</c> with no body. It reads nothing from the request but its method and path.
    /// </summary>
    public static Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        foreach (var (method, route, handle) in Handlers)
        {
            if (HttpMethods.Equals(request.Method, method) && request.Path.Equals(route, StringComparison.Ordinal))
            {
                return handle(context);
            }
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    // 200 with the liveness body.
    private static async Task LiveAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = JsonType;
        context.Response.ContentLength = Live.Length;
        await context.Response.WriteAsync(Live, context.RequestAborted).ConfigureAwait(false);
    }

    // 200 or 503 with the readiness report, which holds check names, reasons, and code-defined feature and module names
    // only (Readiness rule 10).
    private static async Task ReadyAsync(HttpContext context)
    {
        var report = await context.RequestServices.GetRequiredService<ReadinessEvaluator>().EvaluateAsync(context.RequestAborted).ConfigureAwait(false);
        var body = report.ToJson();
        context.Response.StatusCode = report.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = JsonType;
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }
}
