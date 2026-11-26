using System.Globalization;
using System.Text.Json;
using Iskender.Domain.Identity;
using Microsoft.AspNetCore.Http;

namespace Iskender.Management.Security;

/// <summary>
/// <c>GET &lt;prefix&gt;/installation</c>, the one protected route shipped (operator-security contract, Admin routes rule 1,
/// FR-036a): <c>200</c> with exactly <c>{"state":"initialized","initializedAt":"&lt;RFC 3339 UTC&gt;"}</c>, from the status
/// the gate read, and never a principal ID. Owned by ISK-0010.
/// </summary>
internal static class InstallationEndpoint
{
    /// <summary>The route under the prefix.</summary>
    public const string Route = "/installation";

    public static async Task HandleAsync(HttpContext http)
    {
        var status = (InstallationStatus)http.Items[AdminPipeline.InstallationItem]!;
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "application/json";
        await using var writer = new Utf8JsonWriter(http.Response.Body);
        writer.WriteStartObject();
        writer.WriteString("state", "initialized");
        writer.WriteString("initializedAt", Rfc3339(status.InitializedAt!.Value));
        writer.WriteEndObject();
        await writer.FlushAsync(http.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>UTC, with a <c>Z</c> and the fraction's trailing zeros dropped.</summary>
    public static string Rfc3339(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}
