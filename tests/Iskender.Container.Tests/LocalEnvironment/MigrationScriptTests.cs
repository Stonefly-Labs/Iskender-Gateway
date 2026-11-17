using System.Text.Json;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// User Story 6, acceptance scenarios 1 to 4, and FR-018 (local-environment contract, Scripts and Exit codes; container
/// contract, Migration command rule 7): the migration script runs the control-plane image's <c>migrate</c> command once
/// with <c>--no-deps</c>, and before ISK-0021 every operation exits 5 with one event 7006 naming
/// <c>persistence.unavailable</c>.
/// </summary>
[Trait("Category", "Compose")]
public sealed class MigrationScriptTests
{
    private static readonly (string[] Args, string Operation)[] Operations =
    [
        (["apply"], "apply"),
        (["status"], "status"),
        ([], "status"),
        (["reverse", "--steps", "1", "--confirm-data-loss"], "reverse"),
    ];

    [Fact]
    public async Task EveryOperationExits5WithOneEvent7006AndLeavesNoJobOrPostgresChange()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
        Assert.Empty(await MigrateContainersAsync(topology));
        var postgres = await RestartTests.InspectAsync(topology, "postgres");

        foreach (var (args, operation) in Operations)
        {
            var result = await topology.RunScriptAsync("migrate.sh", args);

            Assert.True(result.ExitCode == 5, $"migrate.sh {string.Join(' ', args)} exited {result.ExitCode}: {result.StandardError}");
            var finished = Assert.Single(Events(result.StandardOutput + "\n" + result.StandardError), entry => entry.GetProperty("EventId").GetInt32() == 7006);
            Assert.Equal(operation, finished.GetProperty("State").GetProperty("Operation").GetString());
            Assert.Equal("persistence.unavailable", finished.GetProperty("State").GetProperty("Code").GetString());
            Assert.Empty(await MigrateContainersAsync(topology));
        }

        var unknown = await topology.RunScriptAsync("migrate.sh", ["frobnicate"]);
        Assert.Equal(64, unknown.ExitCode);
        Assert.Empty(await MigrateContainersAsync(topology));

        // --no-deps: postgres is the same container, still publishing nothing (research R2, R11).
        var after = await RestartTests.InspectAsync(topology, "postgres");
        Assert.Equal(postgres.GetProperty("Id").GetString(), after.GetProperty("Id").GetString());
        Assert.DoesNotContain(after.GetProperty("NetworkSettings").GetProperty("Ports").EnumerateObject(), port => port.Value.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task WithNoControlPlaneImageTheScriptExits2()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();

        var result = await topology.RunScriptAsync("migrate.sh", ["status"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("start.sh", result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<string>> MigrateContainersAsync(LocalTopology topology) =>
        (await LocalTopology.DockerAsync("ps", "--all", "--quiet", "--filter", $"label=com.docker.compose.project={topology.Project}", "--filter", "label=com.docker.compose.service=migrate"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static List<JsonElement> Events(string output) =>
        [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('{'))
            .Select(line =>
            {
                using var entry = JsonDocument.Parse(line);
                return entry.RootElement.Clone();
            })
            .Where(entry => entry.TryGetProperty("EventId", out _))];
}
