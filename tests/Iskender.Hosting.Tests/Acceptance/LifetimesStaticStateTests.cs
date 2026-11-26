using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// User Story 4, acceptance scenario 2, and SC-006: no production type holds per-container state in a static field
/// (hosting contract, Static state; research R11). Every assembly under <c>src/</c> is read from its build output.
/// </summary>
public class LifetimesStaticStateTests
{
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly ProjectLoadContext Projects = new();

    private static readonly string[] ImmutableValues =
    [
        "System.String", "System.Decimal", "System.TimeSpan", "System.DateTimeOffset", "System.Guid",
    ];

    private static readonly string[] ImmutableCollectionNamespaces = ["System.Collections.Frozen", "System.Collections.Immutable"];

    private static readonly string[] Instruments =
    [
        "System.Diagnostics.ActivitySource", "System.Diagnostics.Metrics.Meter", "System.Diagnostics.Metrics.Counter`1",
        "System.Diagnostics.Metrics.UpDownCounter`1", "System.Diagnostics.Metrics.Histogram`1", "System.Diagnostics.Metrics.ObservableCounter`1",
        "System.Diagnostics.Metrics.ObservableUpDownCounter`1", "System.Diagnostics.Metrics.ObservableGauge`1",
    ];

    private static readonly string[] RegistryTypes =
    [
        "Iskender.Core.Contracts.Configuration.SectionSchema", "Iskender.Core.Contracts.Errors.ErrorCode",
        "Iskender.Core.Contracts.Identifiers.ResourceType", "Iskender.Core.Contracts.Versioning.WireContract",
    ];

    // The hosting contract's Static state table, by Type.Field.
    private static readonly string[] Listed =
    [
        "Iskender.Domain.Registry.CanonicalWriter.Options",
        "Iskender.Domain.Registry.CanonicalWriter.Utf8",
        "Iskender.Infrastructure.PostgreSql.Migrations.MigrationScript.StrictUtf8",
        "Iskender.Domain.Registry.FieldRules.IgnoreAsciiCase",
        "Iskender.Core.Contracts.Errors.GatewayError.NoDiagnostics",
        "Iskender.Infrastructure.PostgreSql.Classification.Recorded",
        "Iskender.Core.Contracts.Errors.ErrorCategory.<All>k__BackingField",
        "Iskender.Core.Contracts.Errors.ErrorCodes.<All>k__BackingField",
        "Iskender.Core.Contracts.Identifiers.ResourceTypes.<All>k__BackingField",
        "Iskender.Core.Contracts.Versioning.WireContracts.<All>k__BackingField",
        "Iskender.Infrastructure.PostgreSql.Coordination.CoordinationMigrations.<Module>k__BackingField",
        "Iskender.Infrastructure.PostgreSql.Coordination.CoordinationTables.Values",
        "Iskender.Infrastructure.PostgreSql.Coordination.CoordinationTables.Leases",
        "Iskender.Infrastructure.PostgreSql.Coordination.CoordinationTables.Fences",
        "Iskender.Infrastructure.PostgreSql.Coordination.CoordinationTables.CacheGenerations",
        "Iskender.Infrastructure.PostgreSql.PostgreSqlAdapter.<DriverType>k__BackingField",
        "Iskender.Infrastructure.PostgreSql.Directory.DirectoryMigrations.<Module>k__BackingField",
        "Iskender.Infrastructure.PostgreSql.Work.WorkMigrations.<Module>k__BackingField",
        "Iskender.Infrastructure.PostgreSql.Directory.DirectoryTables.Scopes",
        "Iskender.Infrastructure.PostgreSql.Work.WorkTables.Items",
        "Iskender.Infrastructure.PostgreSql.Work.WorkTables.JobRuns",
        "Iskender.Domain.Identity.OperatorActions.<InstallationRead>k__BackingField",
        "Iskender.Domain.Identity.OperatorActions.<All>k__BackingField",
        "Iskender.Domain.Identity.OperatorRoles.<SystemOperator>k__BackingField",
        "Iskender.Domain.Identity.OperatorRoles.<All>k__BackingField",
        "Iskender.Infrastructure.PostgreSql.Audit.AuditMigrations.<Module>k__BackingField",
        "Iskender.Infrastructure.PostgreSql.Identity.IdentityMigrations.<Module>k__BackingField",
        "Iskender.Infrastructure.PostgreSql.Audit.AuditTables.Records",
        "Iskender.Infrastructure.PostgreSql.Identity.IdentityTables.Installation",
        "Iskender.Infrastructure.PostgreSql.Identity.IdentityTables.Principals",
        "Iskender.Infrastructure.PostgreSql.Identity.IdentityTables.OperatorGrants",
        "Iskender.Infrastructure.PostgreSql.Identity.IdentityTables.Tokens",
    ];

    // The RedisScripts script fields, each a RedisScript with a fixed name and text.
    private const string RedisScripts = "Iskender.Infrastructure.Redis.RedisScripts";

    private const string RedisScript = "Iskender.Infrastructure.Redis.RedisScript";

    [Fact]
    public void EveryStaticFieldInProductionCodeHoldsNoPerContainerState()
    {
        var assemblies = ProductionAssemblies();

        var offenders = assemblies
            .SelectMany(LoadableTypes)
            .Where(type => !IsCompilerGenerated(type))
            .SelectMany(type => type.GetFields(Statics))
            .Where(field => !IsAllowed(field))
            .Select(field => $"{field.DeclaringType!.FullName}.{field.Name}: {field.FieldType.FullName ?? field.FieldType.Name}{(field.IsInitOnly ? string.Empty : " (not initonly)")}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(Directory.GetDirectories(Path.Combine(RepositoryRoot(), "src")).Length, assemblies.Count);
        Assert.True(offenders.Count == 0, "Static fields outside the allowed classes:\n" + string.Join('\n', offenders));
    }

    /// <summary>The scan's control: each rule it applies catches the field it is meant to catch.</summary>
    [Fact]
    public void TheScanRejectsStateAndASettableFieldAndAllowsEachClass()
    {
        var fields = typeof(Probe).GetFields(Statics).ToDictionary(field => field.Name);

        Assert.False(IsAllowed(fields[nameof(Probe.Counts)]));
        Assert.False(IsAllowed(fields[nameof(Probe.Settable)]));
        Assert.False(IsAllowed(fields["<Mutable>k__BackingField"]));
        Assert.True(IsAllowed(fields[nameof(Probe.Literal)]));
        Assert.True(IsAllowed(fields[nameof(Probe.Name)]));
        Assert.True(IsAllowed(fields[nameof(Probe.Frozen)]));
        Assert.True(IsAllowed(fields[nameof(Probe.Callback)]));
        Assert.True(IsAllowed(fields[nameof(Probe.Instance)]));
        Assert.True(IsAllowed(fields[nameof(Probe.Source)]));
        Assert.True(IsAllowed(fields["<Fixed>k__BackingField"]));
    }

    private static bool IsAllowed(FieldInfo field)
    {
        if (field.IsLiteral)
        {
            return true;
        }

        if (!field.IsInitOnly)
        {
            return false;
        }

        var type = field.FieldType;
        var name = type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName;
        var declaring = field.DeclaringType!;
        return type.IsPrimitive
            || type.IsEnum
            || ImmutableValues.Contains(name)
            || ImmutableCollectionNamespaces.Contains(type.Namespace)
            || typeof(Delegate).IsAssignableFrom(type)
            || Instruments.Contains(name)
            || (type == declaring && declaring.IsSealed)
            || RegistryTypes.Contains(name)
            || Listed.Contains($"{declaring.FullName}.{field.Name}")
            || (declaring.FullName == RedisScripts && name == RedisScript);
    }

    private static bool IsCompilerGenerated(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.Name.Contains('<', StringComparison.Ordinal) || current.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            Assert.Fail($"{assembly.GetName().Name} has types that cannot be loaded: {string.Join("; ", partial.LoaderExceptions.Select(failure => failure?.ToString()))}");
            throw;
        }
    }

    // Every src/ project's assembly, from the build output matching this test's configuration and target framework.
    private static List<Assembly> ProductionAssemblies()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework = output.Name;
        var configuration = output.Parent!.Name;
        var assemblies = new List<Assembly>();
        foreach (var project in Directory.GetDirectories(Path.Combine(RepositoryRoot(), "src")).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(project);
            var path = Path.Combine(project, "bin", configuration, framework, name + ".dll");
            Assert.True(File.Exists(path), $"{name} has no build output at {path}. Build the solution first.");
            assemblies.Add(Load(path));
        }

        return assemblies;
    }

    // A referenced assembly is the one already loaded. Any other is loaded into the shared project context.
    private static Assembly Load(string path)
    {
        var name = AssemblyName.GetAssemblyName(path);
        if (AssemblyLoadContext.Default.Assemblies.FirstOrDefault(loaded => loaded.GetName().Name == name.Name) is { } loaded)
        {
            return loaded;
        }

        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(name);
        }
        catch (FileNotFoundException)
        {
            return Projects.LoadProject(path);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Iskender.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found.");
    }

    // Every src/ assembly outside this test's own dependencies, with the dependencies each one's build output names. It is
    // not collectible and the test keeps it, so it is never unloaded: a collectible context with no reference left is
    // unloaded by its finalizer, after which resolving a dependency throws (0x80131509), depending on when a GC runs.
    private sealed class ProjectLoadContext() : AssemblyLoadContext(nameof(ProjectLoadContext))
    {
        private readonly List<AssemblyDependencyResolver> _resolvers = [];

        // An assembly already loaded here, as another project's dependency, is that one, not a second copy.
        public Assembly LoadProject(string path)
        {
            var name = AssemblyName.GetAssemblyName(path).Name;
            lock (_resolvers)
            {
                _resolvers.Add(new AssemblyDependencyResolver(path));
                return Assemblies.FirstOrDefault(loaded => loaded.GetName().Name == name) ?? LoadFromAssemblyPath(path);
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (Default.Assemblies.Any(loaded => loaded.GetName().Name == assemblyName.Name))
            {
                return null;
            }

            lock (_resolvers)
            {
                return _resolvers.Select(resolver => resolver.ResolveAssemblyToPath(assemblyName)).FirstOrDefault(resolved => resolved is not null) is { } path
                    ? LoadFromAssemblyPath(path)
                    : null;
            }
        }
    }

    private sealed class Probe
    {
        public const int Literal = 1;

        public static readonly Dictionary<string, long> Counts = [];

        public static readonly string Name = "probe";

        public static readonly System.Collections.Frozen.FrozenSet<string> Frozen = System.Collections.Frozen.FrozenSet<string>.Empty;

        public static readonly Action Callback = () => { };

        public static readonly Probe Instance = new();

        public static readonly System.Diagnostics.ActivitySource Source = new("probe");

        public static int Settable;

        public static int Mutable { get; set; }

        public static TimeSpan Fixed { get; } = TimeSpan.Zero;

        // Assigns the settable field, so it is a real one, which the scan must reject.
        public static void Set(int value) => Settable = value;
    }
}
