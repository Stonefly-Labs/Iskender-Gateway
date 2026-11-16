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

    private static readonly byte[] Live = """{"version":1,"status":"live"}"""u8.ToArray();

    /// <summary>
    /// Answers one request on the health listener: <c>200</c> with the liveness body, <c>200</c> or <c>503</c> with the
    /// readiness report, or <c>404</c>. It reads nothing from the request but its method and path.
    /// </summary>
    public static async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        if (HttpMethods.IsGet(request.Method) && request.Path.Equals(LivePath, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = JsonType;
            context.Response.ContentLength = Live.Length;
            await context.Response.Body.WriteAsync(Live, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsGet(request.Method) && request.Path.Equals(ReadyPath, StringComparison.Ordinal))
        {
            // The report holds check names, reasons, and code-defined feature and module names only (Readiness rule 10).
            var report = await context.RequestServices.GetRequiredService<ReadinessEvaluator>().EvaluateAsync(context.RequestAborted).ConfigureAwait(false);
            var body = report.ToJson();
            context.Response.StatusCode = report.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = JsonType;
            context.Response.ContentLength = body.Length;
            await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }
}
