using System.Text.Json;
using System.Text.RegularExpressions;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Container.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Iskender.Container.Tests.Security;

/// <summary>
/// The anonymous endpoint inventory (ISK-0010 T036, FR-020, US4.1, US4.2, SC-005, operator-security contract, Anonymous
/// endpoint inventory rules 1 and 2): each process is started through <see cref="ContainerEntryPoint"/> with its Entry
/// points rule 2 registrations and routes, every endpoint on its traffic listener and every route of
/// <see cref="HealthEndpoints.Routes"/> on its health listener is enumerated, and an endpoint that declares no operator
/// action must be listed in <c>anonymous-endpoints.json</c>, and every listed entry must be mapped. The admin group's
/// catch-all is not an endpoint (hosting contract, Admin routes rule 4). The worker is a serving variant, which binds only
/// its health listener, so its endpoints are the health routes.
/// </summary>
public partial class AnonymousEndpointInventoryTests
{
    private const string Contract = "specs/0010-system-operator-bootstrap-security-baseline/contracts/operator-security.md";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryAnonymousEndpointIsListedAndEveryListedEntryIsMapped()
    {
        var discovered = new List<Row>();
        discovered.AddRange(await DiscoverAsync("data-plane", PlaneDefinitions.DataPlane()));
        discovered.AddRange(await DiscoverAsync("control-plane", PlaneDefinitions.ControlPlane()));
        discovered.AddRange(Health("worker"));

        Assert.Empty(Problems(await InventoryAsync(), discovered));
    }

    [Fact]
    public async Task AnUnlistedAnonymousEndpointFailsNamingItsProcessListenerMethodAndRoute()
    {
        var plane = PlaneDefinitions.DataPlane();
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
        discovered.AddRange(await DiscoverAsync("control-plane", PlaneDefinitions.ControlPlane()));
        discovered.AddRange(Health("worker"));

        var problem = Assert.Single(Problems(await InventoryAsync(), discovered));

        Assert.Equal("data-plane traffic GET /fixture/unlisted is reachable without authentication and is not listed", problem);
    }

    [Fact]
    public async Task AListedEntryNoProcessMapsFailsNamingIt()
    {
        var discovered = new List<Row>();
        discovered.AddRange(await DiscoverAsync("data-plane", PlaneDefinitions.DataPlane()));
        discovered.AddRange(Health("control-plane"));
        discovered.AddRange(Health("worker"));

        Assert.Equal(
            [
                "control-plane.traffic.bootstrap (control-plane traffic POST /admin/v1/bootstrap) is listed but no process maps it",
                "control-plane.traffic.recovery (control-plane traffic POST /admin/v1/recovery) is listed but no process maps it",
            ],
            Problems(await InventoryAsync(), discovered));
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

    private static async Task<List<Row>> InventoryAsync()
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

    // Starts the plane, captures its traffic listener's route builder, and lists its anonymous endpoints and its health routes.
    private static async Task<List<Row>> DiscoverAsync(string process, ContainerPlaneDefinition plane)
    {
        IEndpointRouteBuilder? traffic = null;
        var definition = plane with
        {
            MapTraffic = app =>
            {
                plane.MapTraffic!(app);
                traffic = app;
            },
        };
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(definition);
        await host.WhenStartedAsync(client);

        var rows = Health(process);
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

        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
        return rows;
    }

    private static List<Row> Health(string process) =>
        [.. HealthEndpoints.Routes.Select(route => new Row("-", process, "health", route.Method, route.Route))];

    [GeneratedRegex(@"^\s*\| `(?<entry>[a-z\-.]+)` \| (?<process>[A-Za-z ]+) \| (?<listener>Health|Traffic) \| (?<method>`[A-Z]+`|Any) \| (?<route>[^|]+?) \| ", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TableRow();

    internal sealed record Row(string Entry, string Process, string Listener, string Method, string Route)
    {
        public bool Matches(Row other) =>
            Process == other.Process && Listener == other.Listener && Method == other.Method && Route == other.Route;
    }
}
