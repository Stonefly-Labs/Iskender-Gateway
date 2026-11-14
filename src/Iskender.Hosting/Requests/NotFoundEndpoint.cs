using Iskender.Core.Contracts.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Requests;

/// <summary>
/// The catch-all of every Iskender route group: 404 with the <c>error-envelope</c> for <c>core.resource.not_found</c>,
/// carrying the request's gateway correlation ID (hosting contract, Route group rule 5). Owned by ISK-0006.
/// </summary>
internal static class NotFoundEndpoint
{
    /// <summary>The envelope's media type.</summary>
    public const string ContentType = "application/json";

    /// <summary>Writes the envelope for the request's context, which <see cref="RequestBootstrap"/> has set.</summary>
    public static Task HandleAsync(HttpContext http)
    {
        var context = http.RequestServices.GetRequiredService<RequestContextHolder>().Required;
        var envelope = ErrorEnvelopeCodec.Write(GatewayError.Create(ErrorCodes.ResourceNotFound, context.Correlation.RequestId, OutputVisibility.None));
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        http.Response.ContentType = ContentType;
        return http.Response.Body.WriteAsync(envelope, http.RequestAborted).AsTask();
    }
}
