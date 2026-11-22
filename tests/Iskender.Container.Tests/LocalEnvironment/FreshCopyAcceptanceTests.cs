using System.Diagnostics;
using System.Text.Json;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// SC-001, SC-004, SC-005, SC-006, and FR-022 (local-environment contract, Test fixtures rule 4): in one clean copy and
/// one project, 5 consecutive cycles of start, smoke, and reset each exit 0, no project container, network, volume, or
/// password file is left after each reset, exactly one port is published, on loopback, from local environment contract
/// version 3 (ISK-0010) two, the control plane's included, and none of the 15 generated
/// passwords appears in any script output, plane log, or tracked file. From local environment contract version 2
/// (ISK-0009 T058, US8.2, US8.3, SC-009), the smoke run includes the worker, whose log in each cycle shows it started its
/// dispatcher and holds no event 5002, a refused feature, and no 7002, a failed start.
/// </summary>
[Trait("Category", "Compose")]
public sealed class FreshCopyAcceptanceTests
{
    private const int Cycles = 5;

    [Fact]
    public async Task FiveCyclesOfStartSmokeAndResetEachSucceedAndLeaveNothing()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        var passwords = new List<string>();
        var logs = new List<string>();

        for (var cycle = 1; cycle <= Cycles; cycle++)
        {
            var clock = Stopwatch.StartNew();
            var start = await topology.RunScriptAsync("start.sh", []);
            Assert.True(start.ExitCode == 0, $"cycle {cycle}: start.sh exited {start.ExitCode}: {start.StandardError}");
            if (cycle == 1)
            {
                string[] expected = [$"127.0.0.1:{topology.ControlPlanePort}", $"127.0.0.1:{topology.DataPlanePort}"];
                Assert.Equal(expected.Order(StringComparer.Ordinal), (await PublishedAsync(topology)).Order(StringComparer.Ordinal));
            }

            var smoke = await topology.RunScriptAsync("smoke.sh", []);
            Assert.True(smoke.ExitCode == 0, $"cycle {cycle}: smoke.sh exited {smoke.ExitCode}: {smoke.StandardError}");

            passwords.AddRange(File.ReadAllLines(topology.SecretsFile).Select(line => line.Split('=', 2)[1]));
            logs.Add(await topology.ComposeOutputAsync("logs", "--no-color"));
            var worker = await topology.ComposeOutputAsync("logs", "--no-color", "--no-log-prefix", "worker");
            Assert.Contains("\"EventId\":8001", worker, StringComparison.Ordinal);
            Assert.DoesNotContain("\"EventId\":5002", worker, StringComparison.Ordinal);
            Assert.DoesNotContain("\"EventId\":7002", worker, StringComparison.Ordinal);

            var reset = await topology.RunScriptAsync("reset.sh", ["--yes"]);
            Assert.True(reset.ExitCode == 0, $"cycle {cycle}: reset.sh exited {reset.ExitCode}: {reset.StandardError}");
            Assert.Empty(await topology.ProjectResourcesAsync());
            Assert.False(File.Exists(topology.SecretsFile));
            TestContext.Current.SendDiagnosticMessage($"Cycle {cycle} of {Cycles} passed in {clock.Elapsed.TotalSeconds:F0} s.");
        }

        Assert.Equal(Cycles * 3, passwords.Distinct(StringComparer.Ordinal).Count());
        var tracked = (await LocalCheckout.GitAsync(topology.Root, "ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = tracked.Select(path => Path.Combine(topology.Root, path)).Where(File.Exists).Select(File.ReadAllText).ToList();
        foreach (var password in passwords)
        {
            Assert.All(topology.Recorded, run =>
            {
                Assert.DoesNotContain(password, run.StandardOutput, StringComparison.Ordinal);
                Assert.DoesNotContain(password, run.StandardError, StringComparison.Ordinal);
            });
            Assert.All(logs, log => Assert.DoesNotContain(password, log, StringComparison.Ordinal));
            Assert.All(files, content => Assert.DoesNotContain(password, content, StringComparison.Ordinal));
        }
    }

    // Every published host binding across the project's containers.
    private static async Task<List<string>> PublishedAsync(LocalTopology topology)
    {
        var published = new List<string>();
        foreach (var id in (await topology.ComposeOutputAsync("ps", "--all", "--quiet")).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var inspected = JsonDocument.Parse(await LocalTopology.DockerAsync("inspect", id.Trim()));
            foreach (var port in inspected.RootElement[0].GetProperty("NetworkSettings").GetProperty("Ports").EnumerateObject().Where(port => port.Value.ValueKind == JsonValueKind.Array))
            {
                published.AddRange(port.Value.EnumerateArray().Select(binding => $"{binding.GetProperty("HostIp").GetString()}:{binding.GetProperty("HostPort").GetString()}"));
            }
        }

        return published;
    }
}
