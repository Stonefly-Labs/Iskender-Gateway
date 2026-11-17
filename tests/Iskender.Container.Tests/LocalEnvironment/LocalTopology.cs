using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// One developer topology project in its own <see cref="LocalCheckout"/> (data-model Test fixtures): a project name
/// <c>iskender-local-t</c> and 12 hexadecimal characters, three free loopback ports, script runs whose output is
/// recorded for value searches, and a disposal that resets the project and removes its image tags even after a failure.
/// </summary>
internal sealed class LocalTopology : IAsyncDisposable
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan ComposeTimeout = TimeSpan.FromMinutes(5);

    private readonly List<DockerResult> _recorded = [];

    private LocalTopology(LocalCheckout checkout)
    {
        Checkout = checkout;
        Project = $"iskender-local-t{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
        DataPlanePort = FreePort();
        PostgresPort = FreePort();
        ValkeyPort = FreePort();
    }

    public LocalCheckout Checkout { get; }

    public string Root => Checkout.Root;

    public string Project { get; }

    public int DataPlanePort { get; }

    public int PostgresPort { get; }

    public int ValkeyPort { get; }

    /// <summary>The project's password file.</summary>
    public string SecretsFile => Path.Combine(Root, "local", ".secrets", $"{Project}.env");

    /// <summary>Every script result so far, for value searches.</summary>
    public IReadOnlyList<DockerResult> Recorded => _recorded;

    /// <summary>A topology in a new clean copy, with <paramref name="prepare"/> applied to the copy's root first.</summary>
    public static async Task<LocalTopology> CreateAsync(Func<string, Task>? prepare = null)
    {
        var checkout = await LocalCheckout.CreateAsync();
        if (prepare is not null)
        {
            await prepare(checkout.Root);
        }

        return new LocalTopology(checkout);
    }

    /// <summary>A free TCP port on <c>127.0.0.1</c>.</summary>
    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Runs <c>bash local/scripts/&lt;script&gt;</c> in the copy with the project's variables, <c>ISKENDER_ENVIRONMENT</c>
    /// removed, and standard input closed unless <paramref name="stdin"/> is given, and records the result.
    /// <paramref name="environment"/> sets further variables, or removes one with <see langword="null"/>.
    /// </summary>
    public async Task<DockerResult> RunScriptAsync(string script, IReadOnlyList<string> args, string? stdin = null, TimeSpan? timeout = null, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var variables = Variables();
        variables["ISKENDER_ENVIRONMENT"] = null;
        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            variables[name] = value;
        }

        var result = await DockerCli.RunProcessAsync(
            RequireLocalTools.Bash,
            [$"local/scripts/{script}", .. args],
            timeout ?? ScriptTimeout,
            CancellationToken.None,
            Root,
            variables,
            stdin);
        _recorded.Add(result);
        return result;
    }

    /// <summary>Runs <c>docker compose</c> for the project with both Compose files.</summary>
    public Task<DockerResult> ComposeAsync(params string[] args) =>
        DockerCli.RunProcessAsync(
            "docker",
            ["compose", "--project-name", Project, "-f", "local/compose.yaml", "-f", "local/compose.dependencies.yaml", .. args],
            ComposeTimeout,
            CancellationToken.None,
            Root,
            Variables());

    /// <summary>Runs <c>docker compose</c> and returns its standard output, failing on a non-zero exit.</summary>
    public async Task<string> ComposeOutputAsync(params string[] args)
    {
        var result = await ComposeAsync(args);
        Assert.True(result.ExitCode == 0, $"docker compose {string.Join(' ', args)} exited {result.ExitCode}: {result.StandardError}");
        return result.StandardOutput;
    }

    /// <summary>Runs <c>docker</c> and returns its standard output, failing on a non-zero exit.</summary>
    public static async Task<string> DockerAsync(params string[] args)
    {
        var result = await DockerCli.RunAsync(args, ComposeTimeout, CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"docker {string.Join(' ', args)} exited {result.ExitCode}: {result.StandardError}");
        return result.StandardOutput;
    }

    /// <summary>The project's running services.</summary>
    public async Task<IReadOnlyList<string>> RunningServicesAsync() =>
        (await ComposeOutputAsync("ps", "--services", "--status", "running"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The IDs of every container, network, and volume labelled with the project's name.</summary>
    public async Task<IReadOnlyList<string>> ProjectResourcesAsync()
    {
        var label = $"label=com.docker.compose.project={Project}";
        var found = new List<string>();
        foreach (var command in new[] { new[] { "ps", "--all", "--quiet", "--filter", label }, ["network", "ls", "--quiet", "--filter", label], ["volume", "ls", "--quiet", "--filter", label] })
        {
            found.AddRange((await DockerAsync(command)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(id => $"{command[0]} {id}"));
        }

        return found;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DockerCli.RunProcessAsync(RequireLocalTools.Bash, ["local/scripts/reset.sh", "--yes"], ScriptTimeout, CancellationToken.None, Root, Variables());
            await DockerCli.RunAsync(["image", "rm", "--force", $"{Project}/data-plane:local", $"{Project}/control-plane:local"], ComposeTimeout, CancellationToken.None);
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Disposal is best effort; the checkout is still removed.
        }
        finally
        {
            await Checkout.DisposeAsync();
        }
    }

    private Dictionary<string, string?> Variables() => new(StringComparer.Ordinal)
    {
        ["ISKENDER_LOCAL_PROJECT"] = Project,
        ["ISKENDER_LOCAL_DATA_PLANE_PORT"] = DataPlanePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["ISKENDER_LOCAL_POSTGRES_PORT"] = PostgresPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["ISKENDER_LOCAL_VALKEY_PORT"] = ValkeyPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
