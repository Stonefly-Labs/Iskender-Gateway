using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// User Story 1, acceptance scenarios 1 to 6, FR-005 to FR-008, FR-015, FR-016, and the Edge Cases (local-environment
/// contract, Services, Hardening, Ports, PostgreSQL, Passwords, Documents rule 1): a first start from a clean copy, the
/// hardened and loopback-only topology it runs, the provisioned roles, and a stop and restart that reuse the passwords.
/// From local environment contract version 2 (ISK-0009 T057, US8.1), the topology's fifth service is the worker, which
/// runs the control-plane image hardened as the planes are, and publishes nothing.
/// </summary>
[Trait("Category", "Compose")]
public sealed partial class RestartTests
{
    public const string PostgresImage = "postgres:17-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994";

    public const string ValkeyImage = "valkey/valkey:8.1.11@sha256:8f836008e0986c36adf50da714c7060cf689dbb335f61fe077939d90fa650809";

    internal static readonly string[] Services = ["control-plane", "data-plane", "postgres", "valkey", "worker"];

    private static readonly string[] Planes = ["data-plane", "control-plane"];

    // The services that run an Iskender image: the planes, and the worker on the control-plane image.
    internal static readonly string[] Iskender = ["data-plane", "control-plane", "worker"];

    private static readonly string[] PasswordNames = ["POSTGRES_PASSWORD", "ISKENDER_LOCAL_RUNTIME_PASSWORD", "ISKENDER_LOCAL_MIGRATION_PASSWORD"];

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task AFirstStartRunsTheHardenedTopologyAndARestartReusesItsPasswords()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();

        var start = await topology.RunScriptAsync("start.sh", [], environment: new Dictionary<string, string?> { ["ISKENDER_ENVIRONMENT"] = "production" });
        Assert.True(start.ExitCode == 0, $"start.sh exited {start.ExitCode}: {start.StandardError}");

        Assert.Equal(Services, await topology.RunningServicesAsync());
        Assert.Equal(Services, (await topology.ComposeOutputAsync("ps", "--all", "--services")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        foreach (var service in Services)
        {
            Assert.Equal("healthy", await HealthAsync(topology, service));
        }

        await AssertHardenedAsync(topology);
        await AssertOnlyTheDataPlanePortIsPublishedAsync(topology);
        foreach (var plane in Planes)
        {
            var revision = await LocalTopology.DockerAsync("image", "inspect", "--format", "{{index .Config.Labels \"org.opencontainers.image.revision\"}}", $"{topology.Project}/{plane}:local");
            Assert.Equal(topology.Checkout.Head, revision.Trim());
        }

        var passwords = AssertPasswordFile(topology);
        await AssertProvisionedAsync(topology);

        var stop = await topology.RunScriptAsync("stop.sh", []);
        Assert.True(stop.ExitCode == 0, $"stop.sh exited {stop.ExitCode}: {stop.StandardError}");
        foreach (var plane in Iskender)
        {
            var state = await InspectAsync(topology, plane);
            Assert.Equal("exited", state.GetProperty("State").GetProperty("Status").GetString());
            Assert.Equal(0, state.GetProperty("State").GetProperty("ExitCode").GetInt32());
        }

        Assert.Equal(2, (await topology.ProjectResourcesAsync()).Count(resource => resource.StartsWith("volume ", StringComparison.Ordinal)));

        var second = await topology.RunScriptAsync("start.sh", []);
        Assert.True(second.ExitCode == 0, $"the second start.sh exited {second.ExitCode}: {second.StandardError}");
        Assert.Equal(passwords, await File.ReadAllBytesAsync(topology.SecretsFile, TestContext.Current.CancellationToken));

        // Running: the data-plane port in use is the project's own, so a third start succeeds (contract, Ports rule 3).
        var third = await topology.RunScriptAsync("start.sh", []);
        Assert.True(third.ExitCode == 0, $"the third start.sh exited {third.ExitCode}: {third.StandardError}");
        Assert.Equal(Services, await topology.RunningServicesAsync());
    }

    [Fact]
    public async Task APasswordFileWithNoVolumeGivesAValidFirstStart()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(topology.SecretsFile)!);
        var content = string.Join(
            '\n',
            PasswordNames.Select(name => $"{name}={Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))}")) + "\n";
        await File.WriteAllTextAsync(topology.SecretsFile, content, TestContext.Current.CancellationToken);

        var start = await topology.RunScriptAsync("start.sh", []);

        Assert.True(start.ExitCode == 0, $"start.sh exited {start.ExitCode}: {start.StandardError}");
        Assert.Equal(content, await File.ReadAllTextAsync(topology.SecretsFile, TestContext.Current.CancellationToken));
        await AssertProvisionedAsync(topology);
    }

    [Fact]
    public async Task ARejectedPlaneDocumentFailsTheStartNamingThePlaneAndItsExitCode()
    {
        await RequireLocalTools.ThatAsync();
        await using var topology = await LocalTopology.CreateAsync(root =>
            File.AppendAllTextAsync(Path.Combine(root, "local", "config", "data-plane", "iskender.yaml"), "  unknownsection:\n    value: 1\n"));

        var start = await topology.RunScriptAsync("start.sh", []);

        Assert.Equal(1, start.ExitCode);
        Assert.Matches(@"service data-plane is exited, exit code 2\b", start.StandardError);
    }

    internal static async Task<string> HealthAsync(LocalTopology topology, string service) =>
        (await InspectAsync(topology, service)).GetProperty("State").GetProperty("Health").GetProperty("Status").GetString()!;

    internal static async Task<JsonElement> InspectAsync(LocalTopology topology, string service)
    {
        var id = (await topology.ComposeOutputAsync("ps", "--all", "--quiet", service)).Trim();
        Assert.False(string.IsNullOrEmpty(id), $"{service} has no container.");
        using var inspected = JsonDocument.Parse(await LocalTopology.DockerAsync("inspect", id));
        return inspected.RootElement[0].Clone();
    }

    private static async Task AssertHardenedAsync(LocalTopology topology)
    {
        Assert.Equal(PostgresImage, (await InspectAsync(topology, "postgres")).GetProperty("Config").GetProperty("Image").GetString());
        Assert.Equal(ValkeyImage, (await InspectAsync(topology, "valkey")).GetProperty("Config").GetProperty("Image").GetString());
        var worker = (await InspectAsync(topology, "worker")).GetProperty("Config");
        Assert.Equal($"{topology.Project}/control-plane:local", worker.GetProperty("Image").GetString());
        Assert.Equal(["work"], worker.GetProperty("Cmd").EnumerateArray().Select(argument => argument.GetString()));
        foreach (var plane in Iskender)
        {
            var container = await InspectAsync(topology, plane);
            var host = container.GetProperty("HostConfig");
            Assert.Equal("1654:1654", container.GetProperty("Config").GetProperty("User").GetString());
            Assert.True(host.GetProperty("ReadonlyRootfs").GetBoolean());
            Assert.True(host.GetProperty("Tmpfs").TryGetProperty("/tmp", out _));
            Assert.Equal(["ALL"], host.GetProperty("CapDrop").EnumerateArray().Select(capability => capability.GetString()));
            Assert.True(host.GetProperty("CapAdd").ValueKind == JsonValueKind.Null || host.GetProperty("CapAdd").GetArrayLength() == 0);
            Assert.Contains("no-new-privileges:true", host.GetProperty("SecurityOpt").EnumerateArray().Select(option => option.GetString()));
            Assert.False(host.GetProperty("Privileged").GetBoolean());
            Assert.NotEqual("host", host.GetProperty("NetworkMode").GetString());
            Assert.Equal(40, container.GetProperty("Config").GetProperty("StopTimeout").GetInt32());

            // ISKENDER_ENVIRONMENT is the container's own, whatever the caller's shell held (contract, Documents rule 1).
            var iskender = container.GetProperty("Config").GetProperty("Env").EnumerateArray().Select(variable => variable.GetString()!).Where(variable => variable.StartsWith("ISKENDER", StringComparison.Ordinal));
            Assert.Equal(["ISKENDER_ENVIRONMENT=development"], iskender);
        }
    }

    internal static async Task AssertOnlyTheDataPlanePortIsPublishedAsync(LocalTopology topology)
    {
        var published = new List<string>();
        foreach (var service in Services)
        {
            var ports = (await InspectAsync(topology, service)).GetProperty("NetworkSettings").GetProperty("Ports");
            foreach (var port in ports.EnumerateObject().Where(port => port.Value.ValueKind == JsonValueKind.Array))
            {
                published.AddRange(port.Value.EnumerateArray().Select(binding => $"{service} {binding.GetProperty("HostIp").GetString()}:{binding.GetProperty("HostPort").GetString()}->{port.Name}"));
            }
        }

        Assert.Equal([$"data-plane 127.0.0.1:{topology.DataPlanePort}->8080/tcp"], published);
    }

    [UnsupportedOSPlatform("windows")]
    private static byte[] AssertPasswordFile(LocalTopology topology)
    {
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(topology.SecretsFile)!));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(topology.SecretsFile));
        var lines = File.ReadAllLines(topology.SecretsFile);
        Assert.Equal(["ISKENDER_LOCAL_MIGRATION_PASSWORD", "ISKENDER_LOCAL_RUNTIME_PASSWORD", "POSTGRES_PASSWORD"], lines.Select(line => line.Split('=')[0]).Order(StringComparer.Ordinal));
        Assert.All(lines, line => Assert.Matches(Password(), line.Split('=', 2)[1]));
        return File.ReadAllBytes(topology.SecretsFile);
    }

    internal static async Task AssertProvisionedAsync(LocalTopology topology)
    {
        Assert.Equal(
            "iskender_local_migrator|t|f|f|f|f\niskender_local_runtime|t|f|f|f|f\niskender_runtime|f|f|f|f|f",
            await SqlAsync(topology, "SELECT rolname, rolcanlogin, rolsuper, rolbypassrls, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname LIKE 'iskender%' ORDER BY rolname"));
        Assert.Equal(
            "iskender_local_runtime|iskender_runtime",
            await SqlAsync(topology, "SELECT r.rolname, g.rolname FROM pg_auth_members a JOIN pg_roles r ON r.oid = a.member JOIN pg_roles g ON g.oid = a.roleid WHERE r.rolname LIKE 'iskender%' OR g.rolname LIKE 'iskender%' ORDER BY 1"));
        Assert.Equal(
            "iskender_local_migrator",
            await SqlAsync(topology, "SELECT rolname FROM pg_roles WHERE rolname LIKE 'iskender%' AND has_database_privilege(rolname, 'iskender_local', 'CREATE') ORDER BY 1"));
        Assert.Equal(
            "0",
            await SqlAsync(topology, "SELECT count(*) FROM pg_namespace WHERE nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast', 'public') AND nspname NOT LIKE 'pg\\_temp\\_%' AND nspname NOT LIKE 'pg\\_toast\\_temp\\_%'"));
        Assert.Equal("0", await SqlAsync(topology, "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public'"));
    }

    private static async Task<string> SqlAsync(LocalTopology topology, string sql) =>
        (await topology.ComposeOutputAsync("exec", "-T", "postgres", "psql", "-U", "postgres", "-d", "iskender_local", "-At", "-c", sql)).Trim();

    [GeneratedRegex("^[A-Za-z0-9]{32}$")]
    private static partial Regex Password();
}
