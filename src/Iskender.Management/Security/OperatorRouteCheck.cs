using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iskender.Management.Security;

/// <summary>
/// The route-declaration check (operator-security contract, Admin routes rule 2, hosting contract Start-up rule 11,
/// research R9): in <see cref="StartingAsync"/>, before any hosted service starts and so before any listener binds, it
/// fails the start with <c>operator.route_undeclared</c> and event 9005 when <c>MapIskenderOperatorSecurity(...)</c> was
/// called more than once or never, or an endpoint in the group declares neither or both of the two conventions. The
/// composition's requirement check runs first, so a host missing a registration fails with that instead. Owned by
/// ISK-0010.
/// </summary>
internal sealed class OperatorRouteCheck(IServiceProvider services, OperatorRoutes routes, OperatorLog log) : IHostedLifecycleService
{
    /// <summary>The route event 9005 names when the group was mapped more than once or never.</summary>
    public const string NoSingleGroup = "(group)";

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // Without a root, the composition check fails the start; with one, its requirement check answers first.
        if (services.GetService<CompositionRoot>() is null)
        {
            return;
        }

        if (services.GetService<CompositionLifecycle>() is { } lifecycle)
        {
            await lifecycle.EnsureComposableAsync(cancellationToken).ConfigureAwait(false);
        }

        var prefixes = routes.Prefixes;
        if (prefixes.Count != 1)
        {
            throw Undeclared(prefixes.Count == 0 ? NoSingleGroup : prefixes[^1]);
        }

        var undeclared = routes.Endpoints()
            .Select(endpoint => endpoint.Metadata.GetMetadata<OperatorGroupEndpoint>())
            .FirstOrDefault(endpoint => endpoint is { Declared: false });
        if (undeclared is not null)
        {
            throw Undeclared(undeclared.Route);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private GatewayException Undeclared(string route)
    {
        log.RouteUndeclared(route);
        return new GatewayException(ErrorCodes.OperatorRouteUndeclared);
    }
}
