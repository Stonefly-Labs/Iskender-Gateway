using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// User Story 4, acceptance scenarios 1 to 5, FR-009, FR-020, and SC-006 (local-environment contract, Scripts, Volumes,
/// Passwords rule 2, Project name rule 2): the reset removes only its own project's containers, network, volumes, and
/// password file, a non-interactive reset without <c>--yes</c> removes nothing, and start refuses a provisioned volume
/// with a missing or malformed password file.
/// </summary>
[Trait("Category", "Compose")]
public sealed class ResetTests
{
    [Fact]
    public async Task AResetRemovesOnlyItsOwnProjectAndTheNextStartIsAFirstStart()
    {
        await RequireLocalTools.ThatAsync();
        var isk0007 = $"isk-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
        var neighbour = $"iskender-local-n{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
        string[] controls = [$"{isk0007}_data", $"{neighbour}_postgres-data"];
        await LocalTopology.DockerAsync("volume", "create", "--label", $"com.docker.compose.project={isk0007}", controls[0]);
        await LocalTopology.DockerAsync("volume", "create", "--label", $"com.docker.compose.project={neighbour}", controls[1]);
        try
        {
            await using var topology = await LocalTopology.CreateAsync();
            Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
            var first = await File.ReadAllLinesAsync(topology.SecretsFile, TestContext.Current.CancellationToken);

            var reset = await topology.RunScriptAsync("reset.sh", ["--yes"]);

            Assert.True(reset.ExitCode == 0, $"reset.sh exited {reset.ExitCode}: {reset.StandardError}");
            Assert.Empty(await topology.ProjectResourcesAsync());
            Assert.False(File.Exists(topology.SecretsFile));
            foreach (var control in controls)
            {
                Assert.Equal(control, (await LocalTopology.DockerAsync("volume", "inspect", "--format", "{{.Name}}", control)).Trim());
            }

            foreach (var plane in new[] { "data-plane", "control-plane" })
            {
                await LocalTopology.DockerAsync("image", "inspect", "--format", "{{.Id}}", $"{topology.Project}/{plane}:local");
            }

            Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
            var second = await File.ReadAllLinesAsync(topology.SecretsFile, TestContext.Current.CancellationToken);
            Assert.Equal(3, second.Length);
            Assert.Empty(second.Intersect(first));
            await RestartTests.AssertProvisionedAsync(topology);
        }
        finally
        {
            await LocalTopology.DockerAsync("volume", "rm", controls[0], controls[1]);
        }
    }

    [Fact]
    public async Task ANonInteractiveResetWithoutYesRemovesNothing()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
        var before = await topology.ProjectResourcesAsync();

        var reset = await topology.RunScriptAsync("reset.sh", []);

        Assert.Equal(64, reset.ExitCode);
        Assert.Equal(before, await topology.ProjectResourcesAsync());
        Assert.True(File.Exists(topology.SecretsFile));
    }

    [Fact]
    public async Task AProvisionedVolumeWithoutItsPasswordFileIsRefused()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
        Assert.Equal(0, (await topology.RunScriptAsync("stop.sh", [])).ExitCode);
        File.Delete(topology.SecretsFile);

        var start = await topology.RunScriptAsync("start.sh", []);

        Assert.Equal(2, start.ExitCode);
        Assert.Contains("reset.sh", start.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(topology.SecretsFile));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task AMalformedPasswordFileIsRefusedUnchangedAndARestoredOneIsMadeOwnerOnly()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Assert.Equal(0, (await topology.RunScriptAsync("start.sh", [])).ExitCode);
        Assert.Equal(0, (await topology.RunScriptAsync("stop.sh", [])).ExitCode);
        var original = await File.ReadAllBytesAsync(topology.SecretsFile, TestContext.Current.CancellationToken);
        var malformed = string.Join('\n', File.ReadAllLines(topology.SecretsFile).Where(line => !line.StartsWith("ISKENDER_LOCAL_MIGRATION_PASSWORD=", StringComparison.Ordinal))) + "\n";
        await File.WriteAllTextAsync(topology.SecretsFile, malformed, TestContext.Current.CancellationToken);

        var refused = await topology.RunScriptAsync("start.sh", []);

        Assert.Equal(2, refused.ExitCode);
        Assert.Contains("reset.sh", refused.StandardError, StringComparison.Ordinal);
        Assert.Equal(malformed, await File.ReadAllTextAsync(topology.SecretsFile, TestContext.Current.CancellationToken));

        await File.WriteAllBytesAsync(topology.SecretsFile, original, TestContext.Current.CancellationToken);
        File.SetUnixFileMode(topology.SecretsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var restored = await topology.RunScriptAsync("start.sh", []);

        Assert.True(restored.ExitCode == 0, $"start.sh exited {restored.ExitCode}: {restored.StandardError}");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(topology.SecretsFile));
        Assert.Equal(original, await File.ReadAllBytesAsync(topology.SecretsFile, TestContext.Current.CancellationToken));
    }
}
