using System.Text.RegularExpressions;
using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// User Story 3 and SC-007 (local-environment contract, Documents and environment safety, Native runs rule 1; research
/// R5, R6): the topology's documents through the configuration engine, in process, with each plane's own registrations
/// as its entry point composes them, and a search of every tracked file under <c>local/</c> for values.
/// </summary>
public sealed partial class DocumentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"iskender-documents-{Guid.NewGuid():N}");

    public DocumentTests() => Directory.CreateDirectory(_root);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Planes() => ["data-plane", "control-plane"];

    public static TheoryData<string, string> PlanesAndEnvironments() =>
        new() { { "data-plane", "production" }, { "data-plane", "staging" }, { "control-plane", "production" }, { "control-plane", "staging" } };

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task EachBaseValidatesAloneWithNoEnvironmentName(string plane)
    {
        using var provider = Compose(plane, await Copy($"local/config/{plane}", "iskender.yaml"), null);

        var snapshot = Validate(provider);

        Assert.Null(snapshot.Layers.OverlaySource);
        Assert.Equal(CoordinatorKind.Local, provider.GetRequiredService<CoordinationSettings>().AtomicCoordinator);
        Assert.Equal(PostgreSqlTlsMode.Require, provider.GetRequiredService<PostgreSqlSettings>().TlsMode);
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task BaseAndOverlayValidateInDevelopmentAndSelectRedisWithTheDefaultTopology(string plane)
    {
        using var provider = Compose(plane, await Copy($"local/config/{plane}", "iskender.yaml", "iskender.development.yaml"), "development");

        var snapshot = Validate(provider);
        var coordination = provider.GetRequiredService<CoordinationSettings>();

        Assert.Equal("iskender.development.yaml", snapshot.Layers.OverlaySource);
        Assert.Equal(CoordinatorKind.Redis, coordination.AtomicCoordinator);
        Assert.Equal(CoordinatorKind.Redis, coordination.LeaseCoordinator);
        Assert.Equal(CoordinatorKind.Redis, coordination.CacheCoordinator);
        Assert.Equal(CoordinationTopology.MultiReplica, coordination.Topology);
        Assert.Equal(["valkey:6379"], coordination.RedisEndpoints);
        Assert.Equal(PostgreSqlTlsMode.Disable, provider.GetRequiredService<PostgreSqlSettings>().TlsMode);
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task TheOverlaysValuesAsABaseAreRejectedWithNoEnvironmentName(string plane)
    {
        var overlay = await File.ReadAllTextAsync(Path.Combine(await RepositoryAsync(), "local", "config", plane, "iskender.development.yaml"), Token);

        using var provider = Compose(plane, await Write(("iskender.yaml", overlay)), null);
        var rejected = Assert.Throws<GatewayException>(() => Validate(provider));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, rejected.Code);
        Assert.Equal(2, Occurrences(rejected, ErrorCodes.ConfigValueUnsafeForEnvironment.Id));

        // The Redis endpoint with no user or password, checked across fields when the coordination settings resolve:
        // with the base's persistence fields present and tlsMode left out, it is the only error.
        var withoutTls = await Write(("iskender.yaml", Merge(await File.ReadAllTextAsync(Path.Combine(await RepositoryAsync(), "local", "config", plane, "iskender.yaml"), Token), TlsLines().Replace(overlay, ""))));
        using var redis = Compose(plane, withoutTls, null);
        var required = Assert.Throws<GatewayException>(() => Validate(redis));
        Assert.Same(ErrorCodes.ConfigFieldRequired, required.Code);
        Assert.StartsWith("iskender.coordination.redis.", Assert.Single(required.Error!.Diagnostics.Values, value => value.Key == "field").Value, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PlanesAndEnvironments))]
    public async Task AnotherEnvironmentNameReadsNoOverlay(string plane, string environment)
    {
        var directory = await Copy($"local/config/{plane}", "iskender.yaml", "iskender.development.yaml");
        using var alone = Compose(plane, await Copy($"local/config/{plane}", "iskender.yaml"), null);
        using var provider = Compose(plane, directory, environment);

        var snapshot = Validate(provider);
        Validate(alone);

        Assert.Null(snapshot.Layers.OverlaySource);
        Assert.Equal(Effective(alone), Effective(provider));
    }

    [Fact]
    public async Task TheControlPlaneRejectsTheDataPlaneDocuments()
    {
        using var provider = Compose("control-plane", await Copy("local/config/data-plane", "iskender.yaml", "iskender.development.yaml"), "development");

        var rejected = Assert.Throws<GatewayException>(() => Validate(provider));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, rejected.Code);
        Assert.Contains(ErrorCodes.ConfigNamespaceUnknown.Id, Codes(rejected), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDataPlaneAcceptsTheControlPlaneDocumentsWhichAloneNameTheMigrationLogin()
    {
        using var provider = Compose("data-plane", await Copy("local/config/control-plane", "iskender.yaml", "iskender.development.yaml"), "development");

        Validate(provider);

        Assert.NotNull(provider.GetRequiredService<PostgreSqlSettings>().MigrationUsername);
        foreach (var file in new[] { "iskender.yaml", "iskender.development.yaml" })
        {
            Assert.DoesNotContain("migration", await File.ReadAllTextAsync(Path.Combine(await RepositoryAsync(), "local", "config", "data-plane", file), Token), StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task EachNativeDocumentDiffersFromItsContainerDocumentOnlyInTheListedLines(string plane)
    {
        var root = await RepositoryAsync();
        string[] baseLines = plane == "control-plane"
            ? ["  container:", "    trafficPort: 8090", "    healthPort: 8091", "    port: 15432"]
            : ["    port: 15432"];

        AssertOnlyChanged(
            Path.Combine(root, "local", "config", plane, "iskender.yaml"),
            Path.Combine(root, "local", "native", plane, "iskender.yaml"),
            [("    host: postgres", "    host: 127.0.0.1")],
            baseLines);
        AssertOnlyChanged(
            Path.Combine(root, "local", "config", plane, "iskender.development.yaml"),
            Path.Combine(root, "local", "native", plane, "iskender.development.yaml"),
            [("        - valkey:6379", "        - 127.0.0.1:16379")],
            []);
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task EachNativePairValidatesInDevelopment(string plane)
    {
        using var provider = Compose(plane, await Copy($"local/native/{plane}", "iskender.yaml", "iskender.development.yaml"), "development");

        Validate(provider);

        Assert.Equal(["127.0.0.1:16379"], provider.GetRequiredService<CoordinationSettings>().RedisEndpoints);
        Assert.Equal(15432, provider.GetRequiredService<PostgreSqlSettings>().Port);
    }

    [Fact]
    public async Task NoTrackedFileUnderLocalHoldsASecretValue()
    {
        RequireLocalTools.RequireBashAndGit();
        var root = await RepositoryAsync();
        var files = (await LocalCheckout.GitAsync(root, "ls-files", "local")).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(root, file), Token);
            if (file.EndsWith(".yaml", StringComparison.Ordinal))
            {
                AssertSecretKeysHoldOnlyReferences(file, lines);
            }

            foreach (var line in lines.Where(line => !line.TrimStart().StartsWith('#')))
            {
                foreach (Match match in LongToken().Matches(line))
                {
                    Assert.True(
                        IsDigestOrReference(line, match),
                        $"{file} holds a string of 20 or more letters and digits outside a comment, a digest, or a secret reference.");
                }
            }
        }

        var ignored = await DockerCli.RunProcessAsync("git", ["-C", root, "check-ignore", "--no-index", "local/.secrets/iskender-local.env"], TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(0, ignored.ExitCode);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [GeneratedRegex("[A-Za-z0-9]{20,}")]
    private static partial Regex LongToken();

    [GeneratedRegex(@"^ *tlsMode: disable\n", RegexOptions.Multiline)]
    private static partial Regex TlsLines();

    [GeneratedRegex("_|(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBreak();

    [GeneratedRegex("^(?<indent> *)(?<key>[A-Za-z_]+):(?<rest>.*)$")]
    private static partial Regex Key();

    private static bool IsDigestOrReference(string line, Match match) =>
        line.Contains($"sha256:{match.Value}", StringComparison.Ordinal) || line.TrimStart().StartsWith("secretRef:", StringComparison.Ordinal);

    // The words of a key name, split at underscores and at lower-to-upper case changes, so valkey is not key.
    private static IEnumerable<string> Words(string key) => WordBreak().Split(key).Where(word => word.Length > 0);

    // Every key named like a secret, at any depth, holds only a mapping with a secretRef string.
    private static void AssertSecretKeysHoldOnlyReferences(string file, string[] lines)
    {
        string[] secretWords = ["password", "secret", "token", "key", "credential"];
        for (var index = 0; index < lines.Length; index++)
        {
            var match = Key().Match(lines[index]);
            if (!match.Success || match.Groups["key"].Value == "secretRef" || !Words(match.Groups["key"].Value).Any(word => secretWords.Contains(word, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var indent = match.Groups["indent"].Length;
            Assert.True(match.Groups["rest"].Value.Trim().Length == 0, $"{file}: a secret-named key holds a scalar.");
            var children = lines.Skip(index + 1).TakeWhile(line => line.Trim().Length == 0 || line.Length - line.TrimStart().Length > indent).Where(line => line.Trim().Length > 0).ToList();
            var child = Assert.Single(children);
            Assert.Matches(@"^ *secretRef: [a-z0-9/_-]+$", child);
        }
    }

    private static void AssertOnlyChanged(string container, string native, (string From, string To)[] changed, string[] added)
    {
        var expected = Uncommented(container).Select(line => changed.FirstOrDefault(pair => pair.From == line) is { To: { } to } ? to : line).ToList();
        var actual = Uncommented(native).Where(line => !added.Contains(line)).ToList();

        Assert.Equal(expected, actual);
        Assert.All(added, line => Assert.Single(Uncommented(native), line));
    }

    private static string[] Uncommented(string file) => [.. File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith('#'))];

    // A base with the overlay's sections merged in, written as one document: the overlay's persistence lines join the
    // base's persistence section, and its coordination section is appended.
    private static string Merge(string baseDocument, string overlay)
    {
        var overlayLines = overlay.Split('\n').Where(line => !line.TrimStart().StartsWith('#') && line.Length > 0).Skip(1).ToList();
        var persistence = overlayLines.SkipWhile(line => line != "  persistence:").Skip(1).TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).ToList();
        var coordination = overlayLines.SkipWhile(line => line != "  coordination:").ToList();
        var lines = baseDocument.Split('\n').Where(line => !line.TrimStart().StartsWith('#') && line.Length > 0).ToList();
        lines.InsertRange(lines.IndexOf("  persistence:") + 1, persistence);
        lines.AddRange(coordination);
        return string.Join('\n', lines) + "\n";
    }

    private static int Occurrences(GatewayException rejected, string code) =>
        Regex.Count(rejected.Error!.Diagnostics.Text ?? "", Regex.Escape(code));

    private static string Codes(GatewayException rejected) => rejected.Error!.Diagnostics.Text ?? "";


    private static Task<string> RepositoryAsync() => ImageBuild.RepositoryRootAsync();

    private static ConfigurationSnapshot Validate(ServiceProvider provider)
    {
        var snapshot = provider.GetRequiredService<ConfigurationSnapshot>();

        // Settings checked across fields when first resolved, as the entry point resolves them before binding.
        provider.GetRequiredService<CoordinationSettings>();
        provider.GetRequiredService<PostgreSqlSettings>();
        return snapshot;
    }

    private static string Effective(ServiceProvider provider)
    {
        var coordination = provider.GetRequiredService<CoordinationSettings>();
        var persistence = provider.GetRequiredService<PostgreSqlSettings>();
        return string.Join(
            '|',
            coordination.Topology,
            coordination.AtomicCoordinator,
            coordination.LeaseCoordinator,
            coordination.CacheCoordinator,
            string.Join(',', coordination.RedisEndpoints),
            coordination.RedisTlsMode,
            persistence.Host,
            persistence.Port,
            persistence.Database,
            persistence.Username,
            persistence.TlsMode,
            persistence.MigrationUsername);
    }

    private static ServiceProvider Compose(string plane, string directory, string? environment)
    {
        var definition = plane == "data-plane" ? PlaneDefinitions.DataPlane() : PlaneDefinitions.ControlPlane();
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ConfigurationRoots.DirectoryVariable] = directory,
            [ConfigurationRoots.EnvironmentVariable] = environment,
        };
        var sources = ConfigurationRoots.Resolve(variables, FileSystemProbe.Instance) with { ReadEnvironmentVariables = false };
        var services = new ServiceCollection();
        services.AddLogging();
        definition.Compose(services, sources);
        return services.BuildServiceProvider();
    }

    private async Task<string> Copy(string from, params string[] files)
    {
        var root = await RepositoryAsync();
        return await Write([.. files.Select(file => (file, File.ReadAllText(Path.Combine(root, from, file))))]);
    }

    private async Task<string> Write(params (string Name, string Content)[] files)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var (name, content) in files)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, name), content);
        }

        return directory;
    }
}
