using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Iskender.Management.Security;

/// <summary>
/// Records each <c>MapIskenderOperatorSecurity(...)</c> call on one service provider, with the route builder it mapped
/// on, so the route check can refuse a second call or none and read the group's endpoints (research R9). The check runs
/// in <c>StartingAsync</c>, before the host builds its request pipeline, so the container's own endpoint data source does
/// not list them yet. A container singleton, never a static. Owned by ISK-0010.
/// </summary>
internal sealed class OperatorRoutes
{
    private readonly Lock _gate = new();
    private readonly List<(string Prefix, IEndpointRouteBuilder Endpoints)> _calls = [];

    /// <summary>The prefixes mapped, in call order.</summary>
    public IReadOnlyList<string> Prefixes
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls.Select(call => call.Prefix)];
            }
        }
    }

    /// <summary>Every endpoint of the route builders the group was mapped on.</summary>
    public IReadOnlyList<Endpoint> Endpoints()
    {
        lock (_gate)
        {
            return [.. _calls.Select(call => call.Endpoints).Distinct().SelectMany(builder => builder.DataSources).SelectMany(source => source.Endpoints)];
        }
    }

    public void Record(string prefix, IEndpointRouteBuilder endpoints)
    {
        lock (_gate)
        {
            _calls.Add((prefix, endpoints));
        }
    }
}
