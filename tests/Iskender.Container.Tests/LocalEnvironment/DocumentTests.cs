using System.Text.RegularExpressions;
using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
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

    /// <summary>The planes and, from local environment contract version 2 (ISK-0009), the worker.</summary>
    public static TheoryData<string> PlanesAndWorker() => ["data-plane", "control-plane", "worker"];

    // The data-plane and control-plane documents as version 3 (ISK-0010) leaves them: ISK-0008's, which version 2 left
    // byte for byte unchanged, with container.trafficTransport upstream in each base and none in each overlay.
    private static readonly (string File, string Sha256)[] VersionOneDocuments =
    [
        ("local/config/data-plane/iskender.yaml", "52560f7fd351f14b9b2335d9e1124e10175a78eebbd21e29854277a70beef356"),
        ("local/config/data-plane/iskender.development.yaml", "ccae4c58118966986fd4c66a54e64fca385b3166ac51118f59461daf46997846"),
        ("local/config/control-plane/iskender.yaml", "896889cbcb6cb02b053908d56a55a14ea9bfcf8dc71283f89cb404022a0d9e90"),
        ("local/config/control-plane/iskender.development.yaml", "ccae4c58118966986fd4c66a54e64fca385b3166ac51118f59461daf46997846"),
        ("local/native/data-plane/iskender.yaml", "16b1f1ffd2facbbb86c6e31bfc68ec9423e34cacadca8c18cdbbd1e5f0bf4e75"),
        ("local/native/data-plane/iskender.development.yaml", "d8c2f4290ae3e8446cd58dc3bc06fa9330c2835b3d1bdf2a6404c09825992f08"),
        ("local/native/control-plane/iskender.yaml", "10ae75f2624372befc117ba65ca1d69316e8e0951c97eb17f73714249cccbece"),
        ("local/native/control-plane/iskender.development.yaml", "d32f6f316150ad74c0a61d4cdee53742a42ee772b7b2eab2ea691687046cd7e6"),
    ];

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
    [MemberData(nameof(PlanesAndWorker))]
    public async Task EachNativeDocumentDiffersFromItsContainerDocumentOnlyInTheListedLines(string plane)
    {
        var root = await RepositoryAsync();
        string[] baseLines = plane switch
        {
            // From version 3 the control plane's base has a container section of its own (ISK-0010).
            "control-plane" => ["    trafficPort: 8090", "    healthPort: 8091", "    port: 15432"],
            "worker" => ["  container:", "    trafficPort: 8092", "    healthPort: 8093", "    port: 15432"],
            _ => ["    port: 15432"],
        };

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
    [MemberData(nameof(PlanesAndWorker))]
    public async Task EachNativePairValidatesInDevelopment(string plane)
    {
        using var provider = Compose(plane, await Copy($"local/native/{plane}", "iskender.yaml", "iskender.development.yaml"), "development");

        Validate(provider);

        Assert.Equal(["127.0.0.1:16379"], provider.GetRequiredService<CoordinationSettings>().RedisEndpoints);
        Assert.Equal(15432, provider.GetRequiredService<PostgreSqlSettings>().Port);
    }

    // ISK-0009 T056 (local environment contract version 2, Documents rules 2, 3, and 5; research R17).
    [Fact]
    public async Task TheWorkerBaseValidatesAloneForTheWorkerPlaneAndHoldsNoMigrationLogin()
    {
        using var provider = Compose("worker", await Copy("local/config/worker", "iskender.yaml"), null);

        var snapshot = Validate(provider);
        PostgreSqlServiceCollectionExtensions.CheckWorkSettings(provider);

        Assert.Null(snapshot.Layers.OverlaySource);
        Assert.Equal(CoordinatorKind.Local, provider.GetRequiredService<CoordinationSettings>().AtomicCoordinator);
        Assert.Equal(PostgreSqlTlsMode.Require, provider.GetRequiredService<PostgreSqlSettings>().TlsMode);
        Assert.Null(provider.GetRequiredService<PostgreSqlSettings>().MigrationUsername);
    }

    [Fact]
    public async Task TheWorkerPairValidatesInDevelopmentAndSelectsPostgreSqlLeases()
    {
        using var provider = Compose("worker", await Copy("local/config/worker", "iskender.yaml", "iskender.development.yaml"), "development");

        var snapshot = Validate(provider);
        PostgreSqlServiceCollectionExtensions.CheckWorkSettings(provider);
        var coordination = provider.GetRequiredService<CoordinationSettings>();

        Assert.Equal("iskender.development.yaml", snapshot.Layers.OverlaySource);
        Assert.Equal(CoordinatorKind.Redis, coordination.AtomicCoordinator);
        Assert.Equal(CoordinatorKind.PostgreSql, coordination.LeaseCoordinator);
        Assert.Equal(CoordinatorKind.Redis, coordination.CacheCoordinator);
        Assert.Equal(CoordinationTopology.MultiReplica, coordination.Topology);
        Assert.Equal(["valkey:6379"], coordination.RedisEndpoints);
        Assert.Equal(PostgreSqlTlsMode.Disable, provider.GetRequiredService<PostgreSqlSettings>().TlsMode);
        Assert.Null(provider.GetRequiredService<PostgreSqlSettings>().MigrationUsername);
    }

    [Fact]
    public async Task TheWorkerDocumentsAreTheControlPlanesWithoutTheMigrationLoginAndWithPostgreSqlLeases()
    {
        var root = await RepositoryAsync();
        string Document(string plane, string name) => Path.Combine(root, "local", "config", plane, name);

        AssertOnlyChanged(
            Document("worker", "iskender.yaml"),
            Document("control-plane", "iskender.yaml"),
            [],
            ["  container:", "    trafficTransport: upstream", "    migration:", "      username: iskender_local_migrator", "      password:", "        secretRef: local/postgres-migration"]);

        // From version 3 the worker's documents hold no transport mode, since it binds no traffic listener (ISK-0010).
        var overlay = Uncommented(Document("control-plane", "iskender.development.yaml")).ToList();
        overlay.RemoveRange(overlay.IndexOf("  container:"), 2);
        overlay[overlay.IndexOf("    leases:") + 1] = "      coordinator: postgresql";
        Assert.Equal(overlay, Uncommented(Document("worker", "iskender.development.yaml")));
    }

    [Fact]
    public async Task TheWorkerRejectsTheDataPlaneDocuments()
    {
        using var provider = Compose("worker", await Copy("local/config/data-plane", "iskender.yaml", "iskender.development.yaml"), "development");

        var rejected = Assert.Throws<GatewayException>(() => Validate(provider));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, rejected.Code);
        Assert.Contains(ErrorCodes.ConfigNamespaceUnknown.Id, Codes(rejected), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDataPlaneAndControlPlaneDocumentsAreByteForByteUnchanged()
    {
        var root = await RepositoryAsync();

        foreach (var (file, sha256) in VersionOneDocuments)
        {
            Assert.Equal((file, sha256), (file, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, file), Token)))));
        }
    }

    // ISK-0010 T051 (US7.3, FR-035): no tracked file holds an operator token's text or a PEM certificate or private key
    // block; tests generate both at run time.
    [Fact]
    public async Task NoTrackedFileHoldsAnOperatorTokenOrAPemBlock()
    {
        var root = await RepositoryAsync();
        var tracked = (await LocalCheckout.GitAsync(root, "ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var token = new Regex("isk[bre]_[0-9a-hjkmnp-tv-z]{26}_[A-Za-z0-9_-]{43}", RegexOptions.CultureInvariant);
        var pem = new Regex("-----BEGIN [A-Z ]*(PRIVATE KEY|CERTIFICATE)-----", RegexOptions.CultureInvariant);

        var found = tracked.Select(path => Path.Combine(root, path)).Where(File.Exists)
            .Where(path => File.ReadAllText(path) is var text && (token.IsMatch(text) || pem.IsMatch(text)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(found);
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
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ConfigurationRoots.DirectoryVariable] = directory,
            [ConfigurationRoots.EnvironmentVariable] = environment,
        };
        var sources = ConfigurationRoots.Resolve(variables, FileSystemProbe.Instance) with { ReadEnvironmentVariables = false };
        var services = new ServiceCollection();
        services.AddLogging();
        Action<IServiceCollection, CompositionSources> compose = plane switch
        {
            "data-plane" => PlaneDefinitions.DataPlane().Compose,
            "control-plane" => PlaneDefinitions.ControlPlane().Compose,
            _ => PlaneDefinitions.Worker().Compose,
        };
        compose(services, sources);
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
