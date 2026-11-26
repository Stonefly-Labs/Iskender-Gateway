using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Http;

namespace Iskender.Architecture.Tests;

/// <summary>
/// The test principal resolver stays in the tests (ISK-0010 T031, FR-043, research R10): no production project
/// references <c>tests/Iskender.Identity.Tests</c>, no production source names <c>TestOperatorPrincipalResolver</c>, and
/// no built production assembly holds a type that implements <c>IOperatorPrincipalResolver</c> or is named
/// <c>TestOperatorPrincipalResolver</c>. This is a check of its own, outside the module dependency rules contract, which
/// stays at version 5 (research R1).
/// </summary>
public class OperatorResolverIsolationTests
{
    private const string TestProject = "Iskender.Identity.Tests";

    private const string TestResolver = "TestOperatorPrincipalResolver";

    private static readonly RepositoryScanResult Scan = RepositoryScan.Run(RepositoryPaths.Root());

    [Fact]
    public void NoProductionProjectReferencesTheIdentityTests()
    {
        var production = Scan.Projects.Where(project => !project.IsUnderTests).ToList();

        Assert.NotEmpty(production);
        Assert.Empty(production
            .SelectMany(project => project.References.Select(reference => (project.Name, reference.Include, reference.TargetName))
                .Concat(project.AssemblyReferences.Select(reference => (project.Name, Include: reference.Value, TargetName: Path.GetFileNameWithoutExtension(reference.Value)))))
            .Where(reference => string.Equals(reference.TargetName, TestProject, StringComparison.OrdinalIgnoreCase)
                || reference.Include.Contains(TestProject, StringComparison.OrdinalIgnoreCase))
            .Select(reference => $"{reference.Name} references {reference.Include}"));
    }

    [Fact]
    public void NoProductionSourceNamesTheTestResolver()
    {
        var root = RepositoryPaths.Root();
        var sources = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToList();

        Assert.NotEmpty(sources);
        Assert.Empty(sources.Where(path => File.ReadAllText(path).Contains(TestResolver, StringComparison.Ordinal)).Select(path => Path.GetRelativePath(root, path)));
    }

    [Fact]
    public void NoBuiltProductionAssemblyHoldsAResolverImplementation()
    {
        var assemblies = AssemblyRules.Locate(Scan, PackagingRules.Configuration());

        Assert.NotEmpty(assemblies);
        Assert.All(assemblies, assembly => Assert.True(File.Exists(assembly.Path), $"{assembly.Project} is not built at {assembly.Path}; build the solution first."));
        Assert.Empty(assemblies.SelectMany(assembly => Resolvers(assembly.Path).Select(type => $"{assembly.Project}: {type}")));
    }

    [Fact]
    public void TheAssemblyCheckFindsAnImplementationAndATypeNamedTheTestResolver()
    {
        var found = Resolvers(typeof(OperatorResolverIsolationTests).Assembly.Location);

        Assert.Contains(typeof(ResolverInATest).FullName!.Replace('+', '/'), found);
        Assert.Contains(typeof(TestOperatorPrincipalResolver).FullName!.Replace('+', '/'), found);
    }

    /// <summary>
    /// The types in the assembly at <paramref name="path"/> that implement <c>IOperatorPrincipalResolver</c> directly, or
    /// are named <c>TestOperatorPrincipalResolver</c>, as <c>namespace.Type</c>, with nested types after a <c>/</c>.
    /// </summary>
    internal static IReadOnlyList<string> Resolvers(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var found = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var implements = type.GetInterfaceImplementations()
                .Select(implementation => reader.GetInterfaceImplementation(implementation).Interface)
                .Any(target => IsResolverInterface(reader, target));
            if (implements || reader.StringComparer.Equals(type.Name, TestResolver))
            {
                found.Add(Name(reader, type));
            }
        }

        return found;
    }

    private static bool IsResolverInterface(MetadataReader reader, EntityHandle target)
    {
        var (space, name) = target.Kind switch
        {
            HandleKind.TypeReference => (reader.GetTypeReference((TypeReferenceHandle)target).Namespace, reader.GetTypeReference((TypeReferenceHandle)target).Name),
            HandleKind.TypeDefinition => (reader.GetTypeDefinition((TypeDefinitionHandle)target).Namespace, reader.GetTypeDefinition((TypeDefinitionHandle)target).Name),
            _ => (default, default),
        };
        return !name.IsNil
            && reader.StringComparer.Equals(name, nameof(IOperatorPrincipalResolver))
            && reader.StringComparer.Equals(space, typeof(IOperatorPrincipalResolver).Namespace!);
    }

    private static string Name(MetadataReader reader, TypeDefinition type)
    {
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil
            ? $"{reader.GetString(type.Namespace)}.{name}"
            : $"{Name(reader, reader.GetTypeDefinition(declaring))}/{name}";
    }

    // The negative fixtures: a resolver implementation, and a type named the test resolver, in this test assembly.
    private sealed class ResolverInATest : IOperatorPrincipalResolver
    {
        public ValueTask<OperatorPrincipalResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(OperatorPrincipalResolution.Unresolved);
    }

    private sealed class TestOperatorPrincipalResolver;
}
