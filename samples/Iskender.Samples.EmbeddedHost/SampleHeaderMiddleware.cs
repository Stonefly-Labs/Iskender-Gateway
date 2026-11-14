namespace Iskender.Samples.EmbeddedHost;

/// <summary>The sample's own middleware, adding a fixed response header to every response. Owned by ISK-0006.</summary>
public sealed class SampleHeaderMiddleware : IMiddleware
{
    public const string HeaderName = "X-Sample";

    public const string HeaderValue = "embedded-host";

    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        context.Response.Headers[HeaderName] = HeaderValue;
        return next(context);
    }
}
