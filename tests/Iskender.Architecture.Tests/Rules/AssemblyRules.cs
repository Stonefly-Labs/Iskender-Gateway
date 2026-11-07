using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>A production project and the path of the assembly its build produced.</summary>
public sealed record BuiltAssembly(string Project, string Path);

/// <summary>
/// Checks the built production assemblies, so that a route the XML and text scans cannot see still fails if it
/// reaches the compiler's output: a reference by any spelling (A13, A19), a renamed assembly (A15), an
/// <c>InternalsVisibleTo</c> grant by any spelling (A16), and nullable analysis switched off (B4). It reads metadata
/// only, with <c>System.Reflection.Metadata</c>, and loads nothing. A test calls it, since it needs a build, and
/// <see cref="RuleRunner"/> does not.
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
