using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting.Requests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Hosting;

/// <summary>Maps Iskender's route group into a host's own endpoints (hosting contract, Route group). Owned by ISK-0006.</summary>
[ContractVersion("hosting", 1)]
public static class IskenderEndpointRouteBuilderExtensions
{
    private const string RequiredBy = "MapIskender";

    /// <summary>
    /// Maps one route group under <paramref name="prefix"/> and returns it. Every endpoint in it first runs the request
    /// bootstrap, and its catch-all answers 404 with the <c>core.resource.not_found</c> envelope. Adds no middleware,
    /// startup filter, or fallback route. A <c>null</c> argument, or a prefix that is empty or does not start with
    /// <c>/</c>, fails with <c>core.value.invalid</c>. Without <c>AddIskender(...)</c>, writes event 6003 and fails with
    /// <c>hosting.registration_missing</c>.
    /// </summary>
    public static RouteGroupBuilder MapIskender(this IEndpointRouteBuilder endpoints, string prefix)
    {
        if (endpoints is null || string.IsNullOrEmpty(prefix) || prefix[0] != '/')
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var services = endpoints.ServiceProvider;
        if (!services.GetServices<CompositionModule>().Any(module => module.Name == IskenderServiceCollectionExtensions.HostingModule))
        {
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger(CompositionLog.Category) ?? NullLogger.Instance;
            throw CompositionLog.RegistrationMissing(logger, IskenderServiceCollectionExtensions.HostingModule, RequiredBy);
        }

        var group = endpoints.MapGroup(prefix);
        group.AddEndpointFilter(new RequestBootstrap());

        // A route handler, not a RequestDelegate, so the group's filters run for it. The highest order is the lowest priority.
        group.Map("{**path}", (Func<HttpContext, Task>)NotFoundEndpoint.HandleAsync).WithOrder(int.MaxValue);
        return group;
    }
}
