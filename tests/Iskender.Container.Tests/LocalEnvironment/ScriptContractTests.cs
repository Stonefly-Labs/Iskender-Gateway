using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// The scripts' contract with no container runtime (local-environment contract, Scripts, Exit codes, Project name,
/// Ports, Layout; FR-001, FR-017, FR-024, research R12): usage errors, the project name and port checks, root finding,
/// prerequisites, line endings, and modes. Each script runs under <c>/bin/bash</c> where it exists, with a <c>PATH</c>
/// holding only the tools the scripts may use and, where a test says so, a <c>docker</c> stub that records each call.
/// </summary>
public sealed class ScriptContractTests(SharedCheckout copy) : IClassFixture<SharedCheckout>
{
    private const string Library = "lib/common.sh";

    /// <summary>The tools the scripts may run, apart from <c>docker</c> (research R12), and <c>bash</c> for nested runs.</summary>
    private static readonly string[] Tools = ["bash", "git", "tr", "head", "sed", "grep", "cat", "mkdir", "chmod", "rm", "mv", "sleep", "date", "dirname"];

    /// <summary>What the <c>docker</c> stub on <c>PATH</c> does.</summary>
    internal enum Docker
    {
        /// <summary>No <c>docker</c> on <c>PATH</c>.</summary>
        Absent,

        /// <summary>Records each call; answers <c>version</c> and <c>compose version --short</c> (5.5.0), and fails anything else.</summary>
        Stub,

        /// <summary>As <see cref="Stub"/>, with Compose 2.23.9.</summary>
        OldCompose,
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("iskender-local")]
    [InlineData("iskender-local-a")]
    [InlineData("iskender-local-abcdefghijklmnopqrstuvw0")]
    public async Task AValidProjectNameIsAccepted(string? name)
    {
        var result = await RunLibraryAsync("local/scripts", ["isk_project"], Env("ISKENDER_LOCAL_PROJECT", name));

        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("iskender-local-")]
    [InlineData("iskender-local-abcdefghijklmnopqrstuvw01")]
    [InlineData("ISK-local")]
    [InlineData("iskender-local-A")]
    [InlineData("isk-0123456789ab")]
    [InlineData("iskender-local_x")]
    [InlineData("iskender-local\niskender-local")]
    public async Task AnInvalidProjectNameExits64(string name)
    {
        var result = await RunLibraryAsync("local/scripts", ["isk_project"], Env("ISKENDER_LOCAL_PROJECT", name));

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("ISKENDER_LOCAL_PROJECT", result.StandardError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1024", 0)]
    [InlineData("65535", 0)]
    [InlineData("1023", 64)]
    [InlineData("65536", 64)]
    [InlineData("abc", 64)]
    [InlineData("-1", 64)]
    [InlineData("0018080", 64)]
    public async Task APortMustBeAnIntegerFrom1024To65535(string value, int expected)
    {
        var result = await RunLibraryAsync("local/scripts", ["isk_port", "ISKENDER_LOCAL_DATA_PLANE_PORT", "18080"], Env("ISKENDER_LOCAL_DATA_PLANE_PORT", value));

        Assert.Equal(expected, result.ExitCode);
    }

    [Theory]
    [InlineData("local/scripts")]
    [InlineData("docs")]
    public async Task TheRootIsFoundFromAnyDirectoryOfTheCheckout(string directory)
    {
        var checkout = await copy.GetAsync();

        var result = await RunLibraryAsync(directory, ["isk_root"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal((await LocalCheckout.GitAsync(checkout.Root, "rev-parse", "--show-toplevel")).Trim(), result.StandardOutput.Trim());
    }

    [Fact]
    public async Task OutsideACheckoutTheRootExits2()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"iskender-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var result = await RunLibraryAsync(outside, ["isk_root"]);

            Assert.Equal(2, result.ExitCode);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task EveryFileUnderLocalIsLfAndEveryScriptIsExecutable()
    {
        RequireLocalTools.RequireBashAndGit();
        var root = await ImageBuild.RepositoryRootAsync();

        var endings = (await LocalCheckout.GitAsync(root, "ls-files", "--eol", "local")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var stages = (await LocalCheckout.GitAsync(root, "ls-files", "--stage", "local")).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(endings);
        Assert.All(endings, line => Assert.StartsWith("i/lf ", line, StringComparison.Ordinal));
        var scripts = stages.Where(line => IsScript(line.Split('\t')[1])).ToList();
        Assert.Contains(scripts, line => line.EndsWith("\tlocal/scripts/reset.sh", StringComparison.Ordinal));
        Assert.All(scripts, line => Assert.StartsWith("100755 ", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheTopologyAndTheIsk0007FixtureReferenceNeitherOther()
    {
        RequireLocalTools.RequireBashAndGit();
        var root = await ImageBuild.RepositoryRootAsync();
        var local = (await LocalCheckout.GitAsync(root, "ls-files", "local")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var fixture = (await LocalCheckout.GitAsync(root, "ls-files", "tests/Iskender.Container.Tests/compose")).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(local);
        Assert.NotEmpty(fixture);
        foreach (var path in fixture)
        {
            Assert.DoesNotContain("local/", Uncommented(Path.Combine(root, path)), StringComparison.Ordinal);
        }

        foreach (var path in local)
        {
            Assert.DoesNotContain("Container.Tests", Uncommented(Path.Combine(root, path)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ResetHelpExits0()
    {
        var result = await RunScriptAsync("reset.sh", ["--help"], Docker.Stub);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: local/scripts/reset.sh", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetWithAnUnknownOptionExits64()
    {
        var result = await RunScriptAsync("reset.sh", ["--force"], Docker.Stub);

        Assert.Equal(64, result.ExitCode);
        Assert.Empty(result.Calls);
    }

    [Fact]
    public async Task ANonInteractiveResetWithoutYesExits64AndRemovesNothing()
    {
        var result = await RunScriptAsync("reset.sh", [], Docker.Stub);

        Assert.Equal(64, result.ExitCode);
        Assert.DoesNotContain(result.Calls, call => call.Contains(" down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AResetForAnIsk0007ProjectNameExits64BeforeRunningDocker()
    {
        var result = await RunScriptAsync("reset.sh", ["--yes"], Docker.Stub, Env("ISKENDER_LOCAL_PROJECT", "isk-0123456789ab"));

        Assert.Equal(64, result.ExitCode);
        Assert.Empty(result.Calls);
    }

    [Theory]
    [InlineData("start.sh")]
    [InlineData("stop.sh")]
    public async Task StartAndStopHelpExits0WithUsageOnStandardOutput(string script)
    {
        var result = await RunScriptAsync(script, ["--help"], Docker.Stub);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Usage: local/scripts/{script}", result.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(result.Calls);
    }

    [Theory]
    [InlineData("start.sh", "--bogus")]
    [InlineData("stop.sh", "extra")]
    public async Task StartAndStopWithAnUnknownArgumentExit64(string script, string argument)
    {
        var result = await RunScriptAsync(script, [argument], Docker.Stub);

        Assert.Equal(64, result.ExitCode);
        Assert.Empty(result.Calls);
    }

    [Theory]
    [InlineData("start.sh")]
    [InlineData("stop.sh")]
    public async Task StartAndStopForAnIsk0007ProjectNameExit64BeforeRunningDocker(string script)
    {
        var result = await RunScriptAsync(script, [], Docker.Stub, Env("ISKENDER_LOCAL_PROJECT", "isk-0123456789ab"));

        Assert.Equal(64, result.ExitCode);
        Assert.Empty(result.Calls);
    }

    [Fact]
    public async Task StartWithAPrivilegedPortExits64BeforeRunningDocker()
    {
        var result = await RunScriptAsync("start.sh", [], Docker.Stub, Env("ISKENDER_LOCAL_DATA_PLANE_PORT", "80"));

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("ISKENDER_LOCAL_DATA_PLANE_PORT", result.StandardError, StringComparison.Ordinal);
        Assert.Empty(result.Calls);
    }

    [Theory]
    [InlineData("start.sh")]
    [InlineData("stop.sh")]
    public async Task StartAndStopWithNoDockerExit2(string script)
    {
        var result = await RunScriptAsync(script, [], Docker.Absent);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("docker", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartWithComposeOlderThan2240Exits2()
    {
        var result = await RunScriptAsync("start.sh", [], Docker.OldCompose);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("2.23.9", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Calls, call => call.StartsWith("build", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MigrateHelpExits0()
    {
        var result = await RunScriptAsync("migrate.sh", ["--help"], Docker.Stub);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: local/scripts/migrate.sh", result.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("apply", "--steps", "1")]
    [InlineData("reverse", "--steps", "0")]
    [InlineData("reverse", "--steps", "x")]
    [InlineData("reverse", "--steps")]
    [InlineData("status", "extra")]
    public async Task AnInvalidMigrationExits64AndStartsNoJob(params string[] args)
    {
        var result = await RunScriptAsync("migrate.sh", args, Docker.Stub);

        Assert.Equal(64, result.ExitCode);
        Assert.DoesNotContain(result.Calls, call => call.Contains(" run ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryScriptParsesUnderBash()
    {
        var checkout = await copy.GetAsync();
        foreach (var script in Directory.EnumerateFiles(Path.Combine(checkout.Root, "local", "scripts"), "*.sh", SearchOption.AllDirectories))
        {
            var result = await DockerCli.RunProcessAsync(RequireLocalTools.Bash, ["-n", script], TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.True(result.ExitCode == 0, $"{script}: {result.StandardError}");
        }
    }

    /// <summary>A script run: its result and the stub's recorded <c>docker</c> calls.</summary>
    internal sealed record ScriptRun(int ExitCode, string StandardOutput, string StandardError, IReadOnlyList<string> Calls);

    /// <summary>Runs <c>local/scripts/&lt;script&gt;</c> in the shared copy with <paramref name="docker"/> on a restricted <c>PATH</c>.</summary>
    internal async Task<ScriptRun> RunScriptAsync(string script, IReadOnlyList<string> args, Docker docker, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var checkout = await copy.GetAsync();
        return await RunAsync(checkout.Root, Path.Combine(checkout.Root, "local", "scripts", script), args, docker, environment);
    }

    internal static Dictionary<string, string?> Env(string name, string? value) => new(StringComparer.Ordinal) { [name] = value };

    private static bool IsScript(string path) =>
        path.EndsWith(".sh", StringComparison.Ordinal) && (Path.GetDirectoryName(path) == "local/scripts" || path.StartsWith("local/postgres/", StringComparison.Ordinal));

    private static string Uncommented(string file) =>
        string.Join('\n', File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith('#')));

    /// <summary>
    /// Runs a small test script that sources the copy's library and calls <paramref name="call"/>, then prints the
    /// working directory. The script is placed in <paramref name="directory"/>, relative to the copy or absolute.
    /// </summary>
    private async Task<ScriptRun> RunLibraryAsync(string directory, IReadOnlyList<string> call, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var checkout = await copy.GetAsync();
        var folder = Path.IsPathRooted(directory) ? directory : Path.Combine(checkout.Root, directory);
        var script = Path.Combine(folder, $"isk-test-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(script, $"#!/usr/bin/env bash\nset -euo pipefail\n. '{Path.Combine(checkout.Root, "local", "scripts", Library)}'\n\"$@\"\npwd\n");
        try
        {
            return await RunAsync(checkout.Root, script, call, Docker.Stub, environment);
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static async Task<ScriptRun> RunAsync(string root, string script, IReadOnlyList<string> args, Docker docker, IReadOnlyDictionary<string, string?>? environment)
    {
        var tools = Path.Combine(Path.GetTempPath(), $"iskender-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tools);
        var log = Path.Combine(tools, "docker.log");
        try
        {
            foreach (var tool in Tools)
            {
                File.CreateSymbolicLink(Path.Combine(tools, tool), RequireLocalTools.Find(tool) ?? throw new InvalidOperationException($"{tool} is not on PATH."));
            }

            if (docker != Docker.Absent)
            {
                var stub = Path.Combine(tools, "docker");
                await File.WriteAllTextAsync(stub, $"""
                    #!/bin/sh
                    printf '%s\n' "$*" >> '{log}'
                    case "$*" in
                      version) exit 0 ;;
                      "compose version --short") printf '%s\n' '{(docker == Docker.OldCompose ? "2.23.9" : "5.5.0")}'; exit 0 ;;
                    esac
                    exit 1

                    """);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }

            var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = tools,
                ["ISKENDER_LOCAL_PROJECT"] = null,
                ["ISKENDER_LOCAL_DATA_PLANE_PORT"] = null,
                ["ISKENDER_LOCAL_POSTGRES_PORT"] = null,
                ["ISKENDER_LOCAL_VALKEY_PORT"] = null,
                ["ISKENDER_ENVIRONMENT"] = null,
            };
            foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
            {
                variables[name] = value;
            }

            var result = await DockerCli.RunProcessAsync(RequireLocalTools.Bash, [script, .. args], TimeSpan.FromMinutes(1), CancellationToken.None, root, variables);
            var calls = File.Exists(log) ? await File.ReadAllLinesAsync(log) : [];
            return new ScriptRun(result.ExitCode, result.StandardOutput, result.StandardError, calls);
        }
        finally
        {
            Directory.Delete(tools, recursive: true);
        }
    }
}
