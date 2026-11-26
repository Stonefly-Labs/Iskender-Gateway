using System.Text.Json;
using System.Text.RegularExpressions;
using Iskender.Container.Tests.Fixture;
using Iskender.ControlPlane.Host;
using Iskender.Gateway.Host;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Container.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Iskender.Container.Tests.Security;

/// <summary>
/// The anonymous endpoint inventory (ISK-0010 T036 and T060, FR-020, US4.1, US4.2, SC-005, operator-security contract,
/// Anonymous endpoint inventory rules 1 and 2): each process is started through <see cref="ContainerEntryPoint"/> with the
/// shipped definition, <see cref="DataPlaneDefinition"/> and <see cref="ControlPlaneDefinition"/>, whose source files the
/// project compiles as they ship, so an endpoint added to a host fails here (CONV-0010-002). Every endpoint on its traffic
/// listener and every route of <see cref="HealthEndpoints.Routes"/> on its health listener is enumerated, and an endpoint
/// that declares no operator action must be listed in <c>anonymous-endpoints.json</c>, and every listed entry must be
/// mapped. The admin group's catch-all is not an endpoint (hosting contract, Admin routes rule 4). The worker is started as
/// the control plane's <c>work</c> serving command, which binds only its health listener, so its endpoints are the health
/// routes, and its traffic port must stay closed.
/// </summary>
public partial class AnonymousEndpointInventoryTests
{
    private const string Contract = "specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md";

    private const string WorkerLeases = ",\"coordination\":{\"leases\":{\"coordinator\":\"postgresql\"}}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryAnonymousEndpointIsListedAndEveryListedEntryIsMapped()
    {
        var discovered = new List<Row>();
        discovered.AddRange(await DiscoverAsync("data-plane", DataPlaneDefinition.Create()));
        discovered.AddRange(await DiscoverAsync("control-plane", ControlPlaneDefinition.Create()));
        discovered.AddRange(await DiscoverAsync("worker", ControlPlaneDefinition.Create(), "work"));

        Assert.Empty(Problems(await InventoryAsync(), discovered));
    }

    [Fact]
    public async Task AnUnlistedAnonymousEndpointOnTheDataPlaneFailsNamingItsProcessListenerMethodAndRoute()
    {
        var plane = DataPlaneDefinition.Create();
        var unlisted = plane with
        {
            MapTraffic = app =>
            {
                app.MapGet("/fixture/unlisted", () => "unlisted");
                plane.MapTraffic!(app);
            },
        };
        var discovered = new List<Row>();
        discovered.AddRange(await DiscoverAsync("data-plane", unlisted));
        discovered.AddRange(await DiscoverAsync("control-plane", ControlPlaneDefinition.Create()));
        discovered.AddRange(await DiscoverAsync("worker", ControlPlaneDefinition.Create(), "work"));

        var problem = Assert.Single(Problems(await InventoryAsync(), discovered));

        Assert.Equal("data-plane traffic GET /fixture/unlisted is reachable without authentication and is not listed", problem);
    }

    // An endpoint on the control plane's traffic listener outside the admin group declares no operator action, and the
    // route-declaration check covers only the group, so only the inventory finds it.
    [Fact]
    public async Task AnUnlistedAnonymousEndpointOnTheControlPlaneOutsideTheAdminGroupFailsNamingIt()
    {
        var plane = ControlPlaneDefinition.Create();
        var unlisted = plane with
        {
            MapTraffic = app =>
            {
                plane.MapTraffic!(app);
                app.MapPost("/fixture/unlisted", () => "unlisted");
            },
        };
        var discovered = new List<Row>();
        discovered.AddRange(await DiscoverAsync("data-plane", DataPlaneDefinition.Create()));
        discovered.AddRange(await DiscoverAsync("control-plane", unlisted));
        discovered.AddRange(await DiscoverAsync("worker", ControlPlaneDefinition.Create(), "work"));

        var problem = Assert.Single(Problems(await InventoryAsync(), discovered));

        Assert.Equal("control-plane traffic POST /fixture/unlisted is reachable without authentication and is not listed", problem);
    }

    [Fact]
    public async Task AListedEntryNoProcessMapsFailsNamingIt()
    {
        var discovered = new List<Row>();
        discovered.AddRange(await DiscoverAsync("data-plane", DataPlaneDefinition.Create()));
        discovered.AddRange(Health("control-plane"));
        discovered.AddRange(await DiscoverAsync("worker", ControlPlaneDefinition.Create(), "work"));

        Assert.Equal(
            [
                "control-plane.traffic.bootstrap (control-plane traffic POST /admin/v1/bootstrap) is listed but no process maps it",
                "control-plane.traffic.recovery (control-plane traffic POST /admin/v1/recovery) is listed but no process maps it",
            ],
            Problems(await InventoryAsync(), discovered));
    }

    // Middleware is not an endpoint, so the enumeration above cannot see it. It cannot occur on a traffic listener: the
    // entry point adds only the health branch and routing, the route-group methods the shipped definitions call take an
    // IEndpointRouteBuilder, which can add endpoints but no middleware, and the definitions call no middleware method on
    // the application themselves, which this scan of their source enforces (FR-020; CONV-0010-010).
    [Theory]
    [InlineData("src/Iskender.Gateway.Host/DataPlaneDefinition.cs")]
    [InlineData("src/Iskender.ControlPlane.Host/ControlPlaneDefinition.cs")]
    public async Task TheShippedDefinitionsAddNoMiddlewareToATrafficListener(string definition)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(await ImageBuild.RepositoryRootAsync(), definition), Token);

        Assert.Empty(MiddlewareCall().Matches(text).Select(match => match.Value));
    }

    // As rule C1 does for the rules contract: the contract's table is the JSON, row for row, with "Any" for the method
    // "*" and "`MapIskender("/")` catch-all" for the route "/{**path}".
    [Fact]
    public async Task TheContractTableMatchesTheJsonRowForRow()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(await ImageBuild.RepositoryRootAsync(), Contract), Token);
        var section = text[text.IndexOf("## Anonymous endpoint inventory", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 4, StringComparison.Ordinal)];

        var table = TableRow().Matches(section)
            .Select(match => new Row(
                match.Groups["entry"].Value,
                match.Groups["process"].Value.ToLowerInvariant().Replace(' ', '-'),
                match.Groups["listener"].Value.ToLowerInvariant(),
                match.Groups["method"].Value == "Any" ? "*" : match.Groups["method"].Value.Trim('`'),
                match.Groups["route"].Value == "`MapIskender(\"/\")` catch-all" ? "/{**path}" : match.Groups["route"].Value.Trim('`')))
            .ToList();

        Assert.Equal(await InventoryAsync(), table);
    }

    [Fact]
    public async Task TheInventoryHoldsExactlyTheNineEntriesOfRule1()
    {
        var inventory = await InventoryAsync();

        Assert.Equal(9, inventory.Count);
        Assert.Equal(inventory.Count, inventory.Select(row => row.Entry).Distinct(StringComparer.Ordinal).Count());
        Assert.All(inventory, row => Assert.StartsWith($"{row.Process}.{row.Listener}.", row.Entry, StringComparison.Ordinal));
    }

    // Each anonymous endpoint not listed, then each listed entry not mapped, by process, listener, method, and route.
    internal static List<string> Problems(IReadOnlyList<Row> inventory, IReadOnlyList<Row> discovered)
    {
        var problems = new List<string>();
        foreach (var row in discovered.Where(found => !inventory.Any(listed => listed.Matches(found))))
        {
            problems.Add($"{row.Process} {row.Listener} {row.Method} {row.Route} is reachable without authentication and is not listed");
        }

        foreach (var row in inventory.Where(listed => !discovered.Any(listed.Matches)))
        {
            problems.Add($"{row.Entry} ({row.Process} {row.Listener} {row.Method} {row.Route}) is listed but no process maps it");
        }

        return problems;
    }

    internal static async Task<List<Row>> InventoryAsync()
    {
        await using var stream = File.OpenRead(Path.Combine(await ImageBuild.RepositoryRootAsync(), "tests/Iskender.Container.Tests/Security/anonymous-endpoints.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream, cancellationToken: Token);
        return [.. entries!.Select(entry => new Row(
            entry.GetProperty("entry").GetString()!,
            entry.GetProperty("process").GetString()!,
            entry.GetProperty("listener").GetString()!,
            entry.GetProperty("method").GetString()!,
            entry.GetProperty("route").GetString()!))];
    }

    // Starts the plane, or its serving command, captures its traffic listener's route builder, and lists its anonymous
    // endpoints and its health routes. A serving command binds no traffic listener, so its routes are never mapped and its
    // traffic port stays closed.
    private static async Task<List<Row>> DiscoverAsync(string process, ContainerPlaneDefinition plane, params string[] args)
    {
        IEndpointRouteBuilder? traffic = null;
        var definition = plane with
        {
            MapTraffic = plane.MapTraffic is null
                ? null
                : app =>
                {
                    plane.MapTraffic(app);
                    traffic = app;
                },
        };
        // The worker needs leases the PostgreSQL coordinator issues (ISK-0009), as its local environment overlay selects.
        using var host = new ContainerTestHost().WithDocument(sections: args.Length > 0 ? PlaneDefinitions.Persistence + WorkerLeases : PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(definition, args);
        await host.WhenStartedAsync(client);

        var rows = Health(process);
        if (args.Length > 0)
        {
            Assert.Null(traffic);
            Assert.False(await ContainerTestHost.IsListeningAsync(host.TrafficPort));
        }
        else
        {
            foreach (var endpoint in traffic!.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
            {
                var metadata = endpoint.Metadata.Select(item => item.GetType().Name).ToHashSet(StringComparer.Ordinal);
                if (metadata.Contains("OperatorUnmatchedEndpoint") || metadata.Contains("OperatorActionRequirement"))
                {
                    continue;
                }

                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods is { Count: > 0 } listed ? listed : ["*"];
                rows.AddRange(methods.Select(method => new Row("-", process, "traffic", method, endpoint.RoutePattern.RawText!)));
            }
        }

        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
        return rows;
    }

    private static List<Row> Health(string process) =>
        [.. HealthEndpoints.Routes.Select(route => new Row("-", process, "health", route.Method, route.Route))];

    [GeneratedRegex(@"\.(Use\w*|UseWhen|Run|Map|MapWhen)\s*[(<]", RegexOptions.CultureInvariant)]
    private static partial Regex MiddlewareCall();

    [GeneratedRegex(@"^\s*\| `(?<entry>[a-z\-.]+)` \| (?<process>[A-Za-z ]+) \| (?<listener>Health|Traffic) \| (?<method>`[A-Z]+`|Any) \| (?<route>[^|]+?) \| ", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TableRow();

    internal sealed record Row(string Entry, string Process, string Listener, string Method, string Route)
    {
        public bool Matches(Row other) =>
            Process == other.Process && Listener == other.Listener && Method == other.Method && Route == other.Route;
    }
}
