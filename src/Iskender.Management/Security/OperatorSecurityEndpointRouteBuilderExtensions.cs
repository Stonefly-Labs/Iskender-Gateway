using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Management.Security;

/// <summary>Maps the admin route group (operator-security contract, Composition rule 3, Admin routes; research R7). Owned by ISK-0010.</summary>
public static partial class OperatorSecurityEndpointRouteBuilderExtensions
{
    private const string RequiredBy = "MapIskenderOperatorSecurity";

    /// <summary>
    /// Maps the admin routes once under <paramref name="prefix"/>, which is <c>/</c> followed by 1 to 64 characters of
    /// <c>a-z</c>, <c>0-9</c>, <c>-</c>, and <c>/</c>, and returns the group. The group's last convention wraps every
    /// endpoint's delegate in the admin pipeline, however it was mapped, and its catch-all answers <c>404</c> with no
    /// body for any request matching no route by both method and path. Another prefix or a <c>null</c> argument fails
    /// with <c>core.value.invalid</c>; without <c>AddIskenderOperatorSecurity()</c>, with
    /// <c>hosting.registration_missing</c>. A second call fails the host's start with <c>operator.route_undeclared</c>.
    /// </summary>
    public static RouteGroupBuilder MapIskenderOperatorSecurity(this IEndpointRouteBuilder endpoints, string prefix)
    {
        if (endpoints is null || prefix is null || !PrefixPattern().IsMatch(prefix))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var services = endpoints.ServiceProvider;
        var routes = services.GetService<OperatorRoutes>();
        if (routes is null || !services.GetServices<CompositionModule>().Any(module => module.Name == OperatorSecurityServiceCollectionExtensions.OperatorSecurityModule))
        {
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger(CompositionLog.Category) ?? NullLogger.Instance;
            throw CompositionLog.RegistrationMissing(logger, OperatorSecurityServiceCollectionExtensions.OperatorSecurityModule, RequiredBy);
        }

        routes.Record(prefix, endpoints);
        var group = endpoints.MapGroup(prefix);
        group.MapGet(InstallationEndpoint.Route, InstallationEndpoint.HandleAsync).RequireOperatorAction(OperatorActions.InstallationRead);
        MapRedemption(group, OperatorTokenPurpose.Bootstrap, "/bootstrap", "control-plane.traffic.bootstrap");
        MapRedemption(group, OperatorTokenPurpose.Recovery, "/recovery", "control-plane.traffic.recovery");

        // Any method, so a known path with another method answers 404 here rather than 405 outside the group.
        group.Map("{**path}", (RequestDelegate)(http =>
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        })).WithMetadata(new OperatorUnmatchedEndpoint()).WithOrder(int.MaxValue);

        ((IEndpointConventionBuilder)group).Finally(Wrap);
        return group;
    }

    // A redemption route: anonymous, outside the gate, with the pipeline's redemption steps as its handler.
    private static void MapRedemption(RouteGroupBuilder group, OperatorTokenPurpose purpose, string route, string reason)
    {
        group.MapPost(route, (RequestDelegate)(http => http.RequestServices.GetRequiredService<AdminPipeline>()
                .RedeemAsync(http, purpose, http.GetEndpoint()?.Metadata.GetMetadata<OperatorGroupEndpoint>()?.Route ?? route)))
            .AllowAnonymousOperatorEndpoint(reason);
    }

    // The group's last convention: record the endpoint's declarations and put the pipeline in front of its handler. A
    // plain RequestDelegate is wrapped here; a route handler has no delegate yet, so the pipeline runs as the first filter
    // the delegate factory applies after this convention.
    private static void Wrap(EndpointBuilder builder)
    {
        if (builder.Metadata.OfType<OperatorUnmatchedEndpoint>().Any())
        {
            return;
        }

        var actions = builder.Metadata.OfType<OperatorActionRequirement>().ToList();
        var anonymous = builder.Metadata.OfType<AnonymousOperatorEndpoint>().ToList();
        var route = (builder as RouteEndpointBuilder)?.RoutePattern.RawText ?? builder.DisplayName ?? "(unnamed)";
        var endpoint = new OperatorGroupEndpoint(route, actions.SingleOrDefault(), anonymous.SingleOrDefault(), actions.Count + anonymous.Count == 1);
        builder.Metadata.Add(endpoint);
        if (builder.RequestDelegate is { } handler)
        {
            builder.RequestDelegate = async http =>
            {
                if (await AdmitAsync(http, endpoint).ConfigureAwait(false))
                {
                    await handler(http).ConfigureAwait(false);
                }
            };
            return;
        }

        builder.FilterFactories.Insert(0, (_, next) => async invocation =>
            await AdmitAsync(invocation.HttpContext, endpoint).ConfigureAwait(false) ? await next(invocation).ConfigureAwait(false) : Results.Empty);
    }

    // An undeclared endpoint fails the host's start, so it answers only a request made without starting the host.
    private static Task<bool> AdmitAsync(HttpContext http, OperatorGroupEndpoint endpoint)
    {
        if (!endpoint.Declared)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.FromResult(false);
        }

        return http.RequestServices.GetRequiredService<AdminPipeline>().AdmitAsync(http, endpoint);
    }

    [GeneratedRegex(@"^/[a-z0-9\-/]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}
