using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>A production project and the path of the assembly its build produced.</summary>
public sealed record BuiltAssembly(string Project, string Path);

/// <summary>A production project and the path of the <c>project.assets.json</c> its restore wrote.</summary>
public sealed record RestoredAssets(string Project, string Path);

/// <summary>
/// Checks the built production assemblies, so that a route the XML and text scans cannot see still fails if it
/// reaches the compiler's output: a reference by any spelling (A13, A19), a renamed assembly (A15), an
/// <c>InternalsVisibleTo</c> grant by any spelling (A16), and nullable analysis switched off (B4). It reads metadata
/// only, with <c>System.Reflection.Metadata</c>, and loads nothing. It also reads each project's restore output, so a
/// framework reference that a package brings in transitively fails A19 (<see cref="CheckFrameworks"/>). A test calls
/// it, since it needs a build, and <see cref="RuleRunner"/> does not.
/// </summary>
public static class AssemblyRules
{
    private const string InternalPrefix = "Iskender.";

    private const string CompilerServices = "System.Runtime.CompilerServices";

    private static readonly string[] NullableAttributes = ["NullableAttribute", "NullableContextAttribute"];

    /// <summary>The assembly each production project builds to, in the given configuration.</summary>
    public static IReadOnlyList<BuiltAssembly> Locate(RepositoryScanResult scan, string configuration)
    {
        var framework = scan.RootFiles
            .Where(file => string.Equals(file.Name, RepositoryScan.RootBuildProps, StringComparison.Ordinal))
            .SelectMany(file => file.Properties)
            .LastOrDefault(property => string.Equals(property.Name, "TargetFramework", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

        return
        [
            .. scan.Projects
                .Where(project => !project.IsUnderTests)
                .Select(project => new BuiltAssembly(
                    project.Name,
                    Path.Combine(scan.Root, Path.GetDirectoryName(project.Path) ?? string.Empty, "bin", configuration, framework, $"{project.Name}.dll"))),
        ];
    }

    /// <summary>The <c>project.assets.json</c> each production project's restore writes.</summary>
    public static IReadOnlyList<RestoredAssets> LocateAssets(RepositoryScanResult scan) =>
    [
        .. scan.Projects
            .Where(project => !project.IsUnderTests)
            .Select(project => new RestoredAssets(project.Name, Path.Combine(scan.Root, Path.GetDirectoryName(project.Path) ?? string.Empty, "obj", "project.assets.json"))),
    ];

    /// <summary>
    /// A19 for each framework reference that restore resolved for a project, directly or through a package, that is in
    /// a category its role denies. The lock file records no framework references, but <c>project.assets.json</c> lists
    /// the project's own under <c>project.frameworks</c> and each package's under <c>targets</c>.
    /// </summary>
    public static IReadOnlyList<Violation> CheckFrameworks(RuleSource source, IEnumerable<RestoredAssets> restored)
    {
        var violations = new List<Violation>();
        foreach (var assets in restored)
        {
            if (!File.Exists(assets.Path))
            {
                violations.Add(source.Violate("C2", assets.Project, assets.Path, "the restore output is missing. Build the solution first"));
                continue;
            }

            IReadOnlyList<(string Framework, string Origin)> frameworks;
            try
            {
                frameworks = Frameworks(assets.Path);
            }
            catch (JsonException exception)
            {
                violations.Add(source.Violate("C2", assets.Project, assets.Path, $"the restore output cannot be read: {exception.Message}"));
                continue;
            }

            foreach (var category in source.Resolve(assets.Project)?.Denied ?? [])
            {
                foreach (var (framework, origin) in frameworks.Where(entry => source.DeniedPackages[category].FrameworkReferences.Contains(entry.Framework, StringComparer.OrdinalIgnoreCase)))
                {
                    violations.Add(source.Violate("A19", assets.Project, framework, $"restore resolves this framework reference through {origin}, and its role denies the {category} category"));
                }
            }
        }

        return Violation.Normalize(violations, source.RuleOrder);
    }

    /// <summary>Each framework reference in a restore output, with what brought it in: the project itself or a package.</summary>
    private static List<(string Framework, string Origin)> Frameworks(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var frameworks = new List<(string Framework, string Origin)>();

        if (root.TryGetProperty("project", out var project) && project.TryGetProperty("frameworks", out var targetFrameworks))
        {
            foreach (var target in targetFrameworks.EnumerateObject())
            {
                if (target.Value.TryGetProperty("frameworkReferences", out var references))
                {
                    frameworks.AddRange(references.EnumerateObject().Select(reference => (reference.Name, "the project itself")));
                }
            }
        }

        if (root.TryGetProperty("targets", out var targets))
        {
            foreach (var library in targets.EnumerateObject().SelectMany(target => target.Value.EnumerateObject()))
            {
                if (library.Value.TryGetProperty("frameworkReferences", out var references))
                {
                    frameworks.AddRange(references.EnumerateArray().Select(reference => (reference.GetString() ?? string.Empty, $"package {library.Name}")));
                }
            }
        }

        return [.. frameworks.Distinct()];
    }

    public static IReadOnlyList<Violation> Check(RuleSource source, RepositoryScanResult scan, IEnumerable<BuiltAssembly> assemblies, string configuration)
    {
        var violations = new List<Violation>();
        var production = scan.Projects.Where(project => !project.IsUnderTests).Select(project => project.Name).ToList();
        var tests = scan.Projects.Where(project => project.IsUnderTests).Select(project => project.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            if (!File.Exists(assembly.Path))
            {
                violations.Add(source.Violate("C2", assembly.Project, assembly.Path, $"the built assembly is missing. Build the solution first, in the {configuration} configuration"));
                continue;
            }

            using var stream = File.OpenRead(assembly.Path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                violations.Add(source.Violate("C2", assembly.Project, assembly.Path, "the built assembly holds no metadata"));
                continue;
            }

            violations.AddRange(Check(source, assembly.Project, pe.GetMetadataReader(), production, tests));
        }

        return Violation.Normalize(violations, source.RuleOrder);
    }

    private static IEnumerable<Violation> Check(RuleSource source, string project, MetadataReader reader, IReadOnlyList<string> production, HashSet<string> tests)
    {
        var name = reader.GetString(reader.GetAssemblyDefinition().Name);
        if (!string.Equals(name, project, StringComparison.Ordinal))
        {
            yield return source.Violate("A15", project, name, "the built assembly's name must equal the project name");
        }

        var definition = source.Resolve(project);
        var allowed = Allowed(source, project, production);
        foreach (var reference in reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)))
        {
            if (reference.StartsWith(InternalPrefix, StringComparison.OrdinalIgnoreCase) && !allowed.Contains(reference))
            {
                yield return source.Violate("A13", project, reference, "the built assembly references an Iskender assembly that its approved references do not reach");
            }

            foreach (var category in definition?.Denied ?? [])
            {
                if (IsDenied(source.DeniedPackages[category], reference))
                {
                    yield return source.Violate("A19", project, reference, $"the built assembly references it, and its role denies the {category} category");
                }
            }
        }

        foreach (var grantee in Grants(reader).Where(grantee => !tests.Contains(grantee)))
        {
            yield return source.Violate("A16", project, grantee, "the built assembly grants InternalsVisibleTo to something other than a test project");
        }

        if (HasReferenceTypedMembers(reader) && !UsesNullableAttributes(reader))
        {
            yield return source.Violate("B4", project, "Nullable", "the built assembly has reference-typed members but no nullable annotations, so nullable analysis was off");
        }
    }

    /// <summary>The projects reachable from this one through approved references in the rule source, which the compiler may name.</summary>
    private static HashSet<string> Allowed(RuleSource source, string project, IReadOnlyList<string> production)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>([project]);
        while (pending.TryDequeue(out var current))
        {
            var entries = source.Resolve(current)?.References ?? [];
            foreach (var target in production.Where(target => entries.Any(entry => source.Matches(entry, target))))
            {
                if (allowed.Add(target))
                {
                    pending.Enqueue(target);
                }
            }
        }

        return allowed;
    }

    /// <summary>A prefix matches the exact name or the name followed by a dot, as for packages.</summary>
    private static bool IsDenied(DeniedCategory category, string assembly) =>
        category.Prefixes.Any(prefix =>
            string.Equals(assembly, prefix, StringComparison.OrdinalIgnoreCase)
            || assembly.StartsWith($"{prefix}.", StringComparison.OrdinalIgnoreCase));

    /// <summary>The name each <c>InternalsVisibleToAttribute</c> on the assembly grants to, up to any comma.</summary>
    private static IEnumerable<string> Grants(MetadataReader reader)
    {
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (!IsAttribute(reader, attribute, "InternalsVisibleToAttribute"))
            {
                continue;
            }

            var blob = reader.GetBlobReader(attribute.Value);
            var grantee = blob.Length > 2 && blob.ReadUInt16() == 1 ? blob.ReadSerializedString() : null;
            yield return grantee?.Split(',')[0].Trim() ?? "(unreadable)";
        }
    }

    private static bool UsesNullableAttributes(MetadataReader reader) =>
        reader.CustomAttributes.Any(handle => NullableAttributes.Any(name => IsAttribute(reader, reader.GetCustomAttribute(handle), name)));

    private static bool IsAttribute(MetadataReader reader, CustomAttribute attribute, string name)
    {
        var type = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };

        return type.Kind switch
        {
            HandleKind.TypeReference => Is(reader, reader.GetTypeReference((TypeReferenceHandle)type).Namespace, reader.GetTypeReference((TypeReferenceHandle)type).Name, name),
            HandleKind.TypeDefinition => Is(reader, reader.GetTypeDefinition((TypeDefinitionHandle)type).Namespace, reader.GetTypeDefinition((TypeDefinitionHandle)type).Name, name),
            _ => false,
        };
    }

    private static bool Is(MetadataReader reader, StringHandle space, StringHandle type, string name) =>
        reader.StringComparer.Equals(space, CompilerServices) && reader.StringComparer.Equals(type, name);

    /// <summary>
    /// Whether a declared method or field, outside what the compiler synthesized, has a reference type in its signature. The
    /// compiler annotates every such member when nullable analysis is on, so the absence of annotations shows it was off.
    /// </summary>
    private static bool HasReferenceTypedMembers(MetadataReader reader)
    {
        var provider = new ReferenceTypeProvider();
        foreach (var type in reader.TypeDefinitions.Select(reader.GetTypeDefinition).Where(type => !IsSynthesized(reader, type.Name, type.GetCustomAttributes())))
        {
            foreach (var method in type.GetMethods().Select(reader.GetMethodDefinition).Where(method => !IsSynthesized(reader, method.Name, method.GetCustomAttributes())))
            {
                var signature = method.DecodeSignature(provider, null);
                if (signature.ReturnType || signature.ParameterTypes.Any(parameter => parameter))
                {
                    return true;
                }
            }

            if (type.GetFields().Select(reader.GetFieldDefinition).Any(field => !IsSynthesized(reader, field.Name, field.GetCustomAttributes()) && field.DecodeSignature(provider, null)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A name with <c>&lt;</c> or a <c>[CompilerGenerated]</c> mark: the compiler wrote it and annotates none of it, such as the <c>&lt;Main&gt;$</c> of top-level statements.</summary>
    private static bool IsSynthesized(MetadataReader reader, StringHandle name, CustomAttributeHandleCollection attributes) =>
        reader.GetString(name).Contains('<', StringComparison.Ordinal)
        || attributes.Any(handle => IsAttribute(reader, reader.GetCustomAttribute(handle), "CompilerGeneratedAttribute"));

    /// <summary>Decodes a signature to whether it contains a reference type, which is what nullable annotations attach to.</summary>
    private sealed class ReferenceTypeProvider : ISignatureTypeProvider<bool, object?>
    {
        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode is PrimitiveTypeCode.String or PrimitiveTypeCode.Object;

        public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => rawTypeKind != (byte)SignatureTypeKind.ValueType;

        public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => rawTypeKind != (byte)SignatureTypeKind.ValueType;

        public bool GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public bool GetSZArrayType(bool elementType) => true;

        public bool GetArrayType(bool elementType, ArrayShape shape) => true;

        public bool GetByReferenceType(bool elementType) => elementType;

        public bool GetPointerType(bool elementType) => false;

        public bool GetPinnedType(bool elementType) => elementType;

        public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;

        public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments) => genericType || typeArguments.Any(argument => argument);

        public bool GetGenericMethodParameter(object? genericContext, int index) => true;

        public bool GetGenericTypeParameter(object? genericContext, int index) => true;

        public bool GetFunctionPointerType(MethodSignature<bool> signature) => false;
    }
}
