using System.Text.Json;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// The bootstrap script against the topology (ISK-0010 T073, FR-035, local-environment contract version 3, Scripts and
/// Exit codes; CONV-0010-015): it runs the control-plane image's <c>bootstrap</c> command once with <c>--rm</c> and
/// <c>--no-deps</c>, and before ISK-0021 each operation exits 5 with one event 7008 naming
/// <c>persistence.unavailable</c>, prints nothing on standard output, since no token was issued, and leaves no job
/// container and postgres unchanged.
/// </summary>
[Trait("Category", "Compose")]
public sealed class BootstrapScriptTests
{
    [Fact]
    public async Task EveryOperationExits5WithNothingOnStandardOutputAndLeavesNoJobOrPostgresChange()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
        var postgres = await RestartTests.InspectAsync(topology, "postgres");

        foreach (var operation in new[] { "recover", "issue" })
        {
            var result = await topology.RunScriptAsync("bootstrap.sh", [operation]);

            Assert.True(result.ExitCode == 5, $"bootstrap.sh {operation} exited {result.ExitCode}: {result.StandardError}");
            Assert.Equal("", result.StandardOutput);
            var finished = Assert.Single(Events(result.StandardError), entry => entry.GetProperty("EventId").GetInt32() == 7008);
            Assert.Equal(operation, finished.GetProperty("State").GetProperty("Operation").GetString());
            Assert.Equal("failed", finished.GetProperty("State").GetProperty("Outcome").GetString());
            Assert.Equal("persistence.unavailable", finished.GetProperty("State").GetProperty("Code").GetString());
            Assert.Empty(await BootstrapContainersAsync(topology));
        }

        // --no-deps: postgres is the same container, still publishing nothing.
        var after = await RestartTests.InspectAsync(topology, "postgres");
        Assert.Equal(postgres.GetProperty("Id").GetString(), after.GetProperty("Id").GetString());
        Assert.DoesNotContain(after.GetProperty("NetworkSettings").GetProperty("Ports").EnumerateObject(), port => port.Value.ValueKind == JsonValueKind.Array);
    }

    private static async Task<IReadOnlyList<string>> BootstrapContainersAsync(LocalTopology topology) =>
        (await LocalTopology.DockerAsync("ps", "--all", "--quiet", "--filter", $"label=com.docker.compose.project={topology.Project}", "--filter", "label=com.docker.compose.service=bootstrap"))
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
