using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Management.Security;

/// <summary>
/// The admin request pipeline that wraps every endpoint's delegate in the group (operator-security contract, Admin routes
/// rules 3 and 5 to 9, research R8). Each request gets a new correlation ID; a protected route then runs the
/// uninitialized gate, principal resolution, and authorization, and the first step that applies answers with the
/// <c>error-envelope</c>; a success runs the handler with <c>Cache-Control: no-store</c>. The gate runs before any
/// principal is resolved. A container singleton, which keeps an <c>initialized</c> answer once read, because the state
/// never moves back, and never keeps another. Owned by ISK-0010.
/// </summary>
internal sealed class AdminPipeline(
    IInstallationState state,
    IOperatorAuthorization authorization,
    IServiceProvider services,
    OperatorLog log,
    OperatorTelemetry telemetry)
{
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
        if (endpoint.Action is { } requirement && !await AuthorizeAsync(http, endpoint.Route, requirement.Action).ConfigureAwait(false))
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

    private async Task<bool> AuthorizeAsync(HttpContext http, string route, OperatorAction action)
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
        OperatorPrincipalResolution resolution;
        try
        {
            var resolver = services.GetService<IOperatorPrincipalResolver>();
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

    private async Task<bool> DenyAsync(HttpContext http, System.Diagnostics.Activity? activity, string route, int status, ErrorCode code, string reason)
    {
        telemetry.RecordDenial(route, reason);
        OperatorTelemetry.End(activity, OperatorTelemetry.Failed, reason);
        await WriteErrorAsync(http, status, code).ConfigureAwait(false);
        return false;
    }
}
