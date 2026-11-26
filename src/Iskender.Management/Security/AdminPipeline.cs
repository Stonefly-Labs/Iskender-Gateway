using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Iskender.Management.Security;

/// <summary>
/// The admin request pipeline that wraps every endpoint's delegate in the group (operator-security contract, Admin routes
/// rules 3 and 5 to 9, research R8). Each request gets a new correlation ID; a protected route then runs the
/// uninitialized gate, principal resolution, and authorization, and the first step that applies answers with the
/// <c>error-envelope</c>; a success runs the handler with <c>Cache-Control: no-store</c>. The gate runs before any
/// principal is resolved. An anonymous endpoint other than the redemptions, such as one an embedder adds, runs the gate
/// only. The two redemption routes, and nothing else, skip the gate and run <see cref="RedeemAsync"/> as their handler:
/// the shared attempt limit, the body check, the token's parse, then the store's unit, the cheapest refusal first. A
/// container singleton, which keeps an <c>initialized</c> answer once read, because the state never moves back, and never
/// keeps another. Owned by ISK-0010.
/// </summary>
internal sealed class AdminPipeline(
    IInstallationState state,
    IOperatorAuthorization authorization,
    IOperatorRedemption redemption,
    RateLimiter attempts,
    ConfigurationRevision revision,
    OperatorLog log,
    OperatorTelemetry telemetry)
{
    /// <summary>The largest redemption body accepted, in bytes.</summary>
    public const int MaxBodyBytes = 4096;

    /// <summary>The <see cref="HttpContext.Items"/> key of the request's correlation ID.</summary>
    public const string CorrelationItem = "Iskender.Operator.Correlation";

    /// <summary>The <see cref="HttpContext.Items"/> key of the installation status the gate read.</summary>
    public const string InstallationItem = "Iskender.Operator.Installation";

    private const string ContentType = "application/json";

    private InstallationStatus? _initialized;

    /// <summary>The request's correlation ID, which the pipeline set.</summary>
    public static RequestCorrelationId Correlation(HttpContext http) => (RequestCorrelationId)http.Items[CorrelationItem]!;

    /// <summary>
    /// Runs the pipeline's steps for <paramref name="endpoint"/>. When one answers, it has written the response and this
    /// returns <c>false</c>; otherwise it sets <c>Cache-Control: no-store</c> and returns <c>true</c>, and the caller runs the
    /// handler.
    /// </summary>
    public async Task<bool> AdmitAsync(HttpContext http, OperatorGroupEndpoint endpoint)
    {
        http.Items[CorrelationItem] = RequestCorrelationId.New();
        if (!endpoint.Redemption && !await AuthorizeAsync(http, endpoint.Route, endpoint.Action?.Action).ConfigureAwait(false))
        {
            return false;
        }

        http.Response.Headers.CacheControl = "no-store";
        return true;
    }

    /// <summary>Writes the <c>error-envelope</c> for <paramref name="code"/> with status <paramref name="status"/>.</summary>
    public static Task WriteErrorAsync(HttpContext http, int status, ErrorCode code)
    {
        var envelope = ErrorEnvelopeCodec.Write(GatewayError.Create(code, Correlation(http), OutputVisibility.None));
        http.Response.StatusCode = status;
        http.Response.ContentType = ContentType;
        return http.Response.Body.WriteAsync(envelope, http.RequestAborted).AsTask();
    }

    /// <summary>
    /// The handler of the <paramref name="purpose"/> redemption route <paramref name="route"/> (operator-security contract,
    /// Redemption rules 3 to 5 and 8, research R8). Every refusal of a body or token is the uniform <c>403</c>
    /// <c>operator.redemption_failed</c>, whose body differs between failures only in its correlation ID.
    /// </summary>
    public async Task RedeemAsync(HttpContext http, OperatorTokenPurpose purpose, string route)
    {
        var cancellation = http.RequestAborted;
        using var activity = OperatorTelemetry.StartRedeem(purpose);
        using var lease = attempts.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            log.RedemptionLimited();
            telemetry.RecordDenial(route, OperatorDenials.Limited);
            OperatorTelemetry.End(activity, OperatorTelemetry.Failed, OperatorDenials.Limited);
            await WriteErrorAsync(http, StatusCodes.Status429TooManyRequests, ErrorCodes.OperatorRedemptionLimited).ConfigureAwait(false);
            return;
        }

        if (await ReadTokenAsync(http.Request, cancellation).ConfigureAwait(false) is not { } text)
        {
            telemetry.RecordDenial(route, OperatorDenials.RejectedBody);
            await FailAsync(http, activity, purpose, RedemptionFailureReason.Malformed).ConfigureAwait(false);
            return;
        }

        if (!OperatorTokenText.TryParse(text, out var token))
        {
            await FailAsync(http, activity, purpose, RedemptionFailureReason.Malformed).ConfigureAwait(false);
            return;
        }

        RedemptionOutcome outcome;
        try
        {
            var context = new OperatorRequestContext(Correlation(http), revision, OperatorActor.Anonymous);
            outcome = await redemption.RedeemAsync(purpose, token, context, cancellation).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            log.StoreUnavailable(route, ErrorCodes.OperatorStateUnavailable);
            telemetry.RecordDenial(route, OperatorDenials.Unavailable);
            OperatorTelemetry.End(activity, OperatorTelemetry.Failed, OperatorDenials.Unavailable);
            await WriteErrorAsync(http, StatusCodes.Status503ServiceUnavailable, ErrorCodes.OperatorStateUnavailable).ConfigureAwait(false);
            return;
        }

        if (!outcome.Succeeded)
        {
            await FailAsync(http, activity, purpose, outcome.FailureReason.Value).ConfigureAwait(false);
            return;
        }

        log.RedemptionSucceeded(purpose);
        telemetry.RecordRedemption(purpose, reason: null);
        OperatorTelemetry.End(activity, OperatorTelemetry.Succeeded, OperatorTelemetry.None);
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = ContentType;
        await using var writer = new Utf8JsonWriter(http.Response.Body);
        writer.WriteStartObject();
        writer.WriteString("principalId", outcome.Principal.Value);
        writer.WriteString("enrollmentCode", outcome.EnrollmentCode.Reveal());
        writer.WriteEndObject();
        await writer.FlushAsync(cancellation).ConfigureAwait(false);
    }

    // The token of a body that is application/json, at most MaxBodyBytes, and exactly {"token":"<string>"}; otherwise null.
    private static async Task<string?> ReadTokenAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
            || !string.Equals(media.MediaType.Value, ContentType, StringComparison.OrdinalIgnoreCase)
            || request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(MaxBodyBytes + 1);
        try
        {
            var length = 0;
            int read;
            while (length <= MaxBodyBytes && (read = await request.Body.ReadAsync(buffer.AsMemory(length, MaxBodyBytes + 1 - length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                length += read;
            }

            return length > MaxBodyBytes ? null : ParseToken(buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static string? ParseToken(ReadOnlySpan<byte> body)
    {
        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject
                || !reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("token"u8)
                || !reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                return null;
            }

            var token = reader.GetString();
            return reader.Read() && reader.TokenType == JsonTokenType.EndObject && !reader.Read() ? token : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task FailAsync(HttpContext http, Activity? activity, OperatorTokenPurpose purpose, RedemptionFailureReason reason)
    {
        log.RedemptionFailed(purpose, reason);
        telemetry.RecordRedemption(purpose, reason);
        OperatorTelemetry.End(activity, OperatorTelemetry.Failed, reason.ToCode());
        await WriteErrorAsync(http, StatusCodes.Status403Forbidden, ErrorCodes.OperatorRedemptionFailed).ConfigureAwait(false);
    }

    // The gate, then, when the endpoint requires an action, principal resolution and authorization. An anonymous endpoint
    // that is not a redemption passes with the gate alone (Admin routes rules 5 and 6; CONV-0010-021).
    private async Task<bool> AuthorizeAsync(HttpContext http, string route, OperatorAction? action)
    {
        var cancellation = http.RequestAborted;
        var activity = OperatorTelemetry.StartAuthorize(route);

        InstallationStatus status;
        try
        {
            status = await GateAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            log.StoreUnavailable(route, ErrorCodes.OperatorStateUnavailable);
            return await DenyAsync(http, activity, route, StatusCodes.Status503ServiceUnavailable, ErrorCodes.OperatorStateUnavailable, OperatorDenials.Unavailable).ConfigureAwait(false);
        }

        if (!status.IsInitialized)
        {
            log.RequestDenied(route, OperatorDenials.Uninitialized);
            return await DenyAsync(http, activity, route, StatusCodes.Status403Forbidden, ErrorCodes.OperatorUninitialized, OperatorDenials.Uninitialized).ConfigureAwait(false);
        }

        http.Items[InstallationItem] = status;
        if (action is null)
        {
            OperatorTelemetry.End(activity, OperatorTelemetry.Succeeded, OperatorTelemetry.None);
            return true;
        }

        OperatorPrincipalResolution resolution;
        try
        {
            // From the request's scope, not the root provider, so a resolver may be registered with any lifetime, scoped
            // included, without becoming a captive of this singleton (hosting contract, Embedding rule 3; CONV-0010-011).
            var resolver = http.RequestServices.GetService<IOperatorPrincipalResolver>();
            resolution = resolver is null ? OperatorPrincipalResolution.Unresolved : await resolver.ResolveAsync(http, cancellation).ConfigureAwait(false);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            log.ResolutionFailed(failure);
            return await DenyAsync(http, activity, route, StatusCodes.Status503ServiceUnavailable, ErrorCodes.OperatorAuthorizationUnavailable, OperatorDenials.Unavailable).ConfigureAwait(false);
        }

        if (resolution.Principal is not { } principal)
        {
            log.RequestDenied(route, OperatorDenials.Unauthenticated);
            return await DenyAsync(http, activity, route, StatusCodes.Status401Unauthorized, ErrorCodes.IdentityUnauthenticated, OperatorDenials.Unauthenticated).ConfigureAwait(false);
        }

        OperatorDecision decision;
        try
        {
            decision = await authorization.DecideAsync(principal, action, cancellation).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            log.StoreUnavailable(route, ErrorCodes.OperatorAuthorizationUnavailable);
            return await DenyAsync(http, activity, route, StatusCodes.Status503ServiceUnavailable, ErrorCodes.OperatorAuthorizationUnavailable, OperatorDenials.Unavailable).ConfigureAwait(false);
        }

        if (decision != OperatorDecision.Allowed)
        {
            log.RequestDenied(route, OperatorDenials.Forbidden);
            return await DenyAsync(http, activity, route, StatusCodes.Status403Forbidden, ErrorCodes.IdentityAuthDenied, OperatorDenials.Forbidden).ConfigureAwait(false);
        }

        OperatorTelemetry.End(activity, OperatorTelemetry.Succeeded, OperatorTelemetry.None);
        return true;
    }

    // An initialized answer is kept once read; any other answer is read again on the next request (Installation state rule 4).
    private async ValueTask<InstallationStatus> GateAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized) is { } known)
        {
            return known;
        }

        var status = await state.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (status.IsInitialized)
        {
            Volatile.Write(ref _initialized, status);
        }

        return status;
    }

    private async Task<bool> DenyAsync(HttpContext http, Activity? activity, string route, int status, ErrorCode code, string reason)
    {
        telemetry.RecordDenial(route, reason);
        OperatorTelemetry.End(activity, OperatorTelemetry.Failed, reason);
        await WriteErrorAsync(http, status, code).ConfigureAwait(false);
        return false;
    }
}
