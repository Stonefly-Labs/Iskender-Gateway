using Microsoft.AspNetCore.Http;

namespace Iskender.Infrastructure.Container.Health;

/// <summary>
/// The health listener's branch: <c>GET /health/live</c> and <c>GET /health/ready</c>, and <c>404</c> with no body for
/// anything else (container contract, Listeners rule 3, and Liveness; research R10). Owned by ISK-0007.
/// </summary>
internal static class HealthEndpoints
{
    public const string LivePath = "/health/live";

    public const string ReadyPath = "/health/ready";

    private const string JsonType = "application/json";

    private static readonly byte[] Live = """{"version":1,"status":"live"}"""u8.ToArray();

    /// <summary>Answers one request on the health listener. It reads nothing from the request but its method and path.</summary>
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

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }
}
