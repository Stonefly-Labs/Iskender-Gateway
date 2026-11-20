namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// User Story 2, acceptance scenarios 1, 4, 5, 7, and 8, and SC-003 (local-environment contract, Smoke assertions): the
/// smoke script passes on the running topology, fails naming the check and the reason found when Valkey or PostgreSQL
/// stops, fails naming every service when nothing runs, and its output and the plane logs hold no password.
/// </summary>
[Trait("Category", "Compose")]
public sealed class SmokeFailureTests
{
    private static readonly string[] Planes = ["data-plane", "control-plane"];

    [Fact]
    public async Task SmokeFailsNamingTheCheckWhenADependencyStopsAndPassesWhenRestored()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);

        var passed = await topology.RunScriptAsync("smoke.sh", []);
        Assert.True(passed.ExitCode == 0, $"smoke.sh exited {passed.ExitCode}: {passed.StandardError}");

        await topology.ComposeOutputAsync("stop", "valkey");
        var withoutValkey = await topology.RunScriptAsync("smoke.sh", []);
        Assert.Equal(1, withoutValkey.ExitCode);
        foreach (var plane in Planes)
        {
            Assert.Contains($"{plane} readiness: coordination: expected ok, found ", withoutValkey.StandardError, StringComparison.Ordinal);
        }

        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
        var restored = await topology.RunScriptAsync("smoke.sh", []);
        Assert.True(restored.ExitCode == 0, $"smoke.sh after restoring valkey exited {restored.ExitCode}: {restored.StandardError}");

        await topology.ComposeOutputAsync("stop", "postgres");
        var withoutPostgres = await topology.RunScriptAsync("smoke.sh", []);
        Assert.Equal(1, withoutPostgres.ExitCode);
        foreach (var plane in Planes)
        {
            Assert.Contains($"{plane} readiness: persistence: expected authentication_failed, found ", withoutPostgres.StandardError, StringComparison.Ordinal);
        }

        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);

        var passwords = File.ReadAllLines(topology.SecretsFile).Select(line => line.Split('=', 2)[1]).ToList();
        var logs = await topology.ComposeOutputAsync("logs", "--no-color");
        Assert.Equal(3, passwords.Count);
        foreach (var password in passwords)
        {
            Assert.DoesNotContain(password, logs, StringComparison.Ordinal);
            Assert.All(topology.Recorded, run =>
            {
                Assert.DoesNotContain(password, run.StandardOutput, StringComparison.Ordinal);
                Assert.DoesNotContain(password, run.StandardError, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public async Task WithNothingRunningSmokeFailsNamingEveryService()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();

        var result = await topology.RunScriptAsync("smoke.sh", []);

        Assert.Equal(1, result.ExitCode);
        foreach (var service in new[] { "postgres", "valkey", "data-plane", "control-plane", "worker" })
        {
            Assert.Contains($"smoke: {service}: not running", result.StandardError, StringComparison.Ordinal);
        }
    }
}
