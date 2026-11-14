using System.Reflection;
using System.Text.Json;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>
/// H5, H6, and H7, by reflection over the production projects: every project in the ISK-0001 rule source except the
/// executables, which rule A2 forbids any project from referencing, and the samples, which ship in no package and are not
/// production code. The executables' gap is a known limit in the contract.
/// </summary>
public static class DeclarationRules
{
    public const string ContextNamespace = "Iskender.Core.Contracts.Context";

    public static IReadOnlyList<string> DeniedNamespaces { get; } =
        ["Microsoft.AspNetCore", "Microsoft.Extensions.DependencyInjection", "System.Net", "System.Data", "System.IO", "Azure", "Amazon", "Google", "OpenAI", "Anthropic"];

    public static IReadOnlyList<string> DeniedNameEndings { get; } = ["Credential", "Secret", "Connection"];

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Runs H5, H6, and H7 on the production projects. A project the harness cannot load fails H1.</summary>
    public static IReadOnlyList<Violation> Check(IFileSet current)
    {
        var violations = new List<Violation>();
        var types = new List<Type>();
        foreach (var project in ProductionProjects(current))
        {
            try
            {
                types.AddRange(Assembly.Load(new AssemblyName(project)).GetTypes());
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                violations.Add(new Violation(
                    "H1", "public-contracts", RuleCatalog.ContractVersion, $"project {project}",
                    "the harness does not reference this production project, so H5 and H6 cannot check it", HarnessRun.RuleSourcePath, current.Name));
            }
        }

        violations.AddRange(H5(types, current.Name));
        violations.AddRange(H6(types, RuleCatalog.OwnershipResolverAllowList, current.Name));
        violations.AddRange(H7(types.Where(type => type.Namespace == ContextNamespace), current.Name));
        return violations;
    }

    /// <summary>The projects of the rule source other than executables and samples, without placeholder names such as <c>Iskender.Providers.&lt;Provider&gt;</c>.</summary>
    public static IReadOnlyList<string> ProductionProjects(IFileSet current)
    {
        if (current.Read(HarnessRun.RuleSourcePath) is not { } source)
        {
            return [];
        }

        using var document = JsonDocument.Parse(source);
        return document.RootElement.GetProperty("projects").EnumerateArray()
            .Where(project => project.GetProperty("role").GetString() is not ("executable" or "sample"))
            .Select(project => project.GetProperty("name").GetString()!)
            .Where(name => !name.Contains('<', StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// H5: a static field or property of type <see cref="ErrorCode"/> or <see cref="ResourceType"/>, or a constructor call
    /// that creates one, outside <see cref="ErrorCodes"/> and <see cref="ResourceTypes"/> and their nested types.
    /// </summary>
    public static IReadOnlyList<Violation> H5(IEnumerable<Type> types, string checkout)
    {
        var violations = new List<Violation>();
        var constructors = typeof(ErrorCode).GetConstructors(Declared).Concat(typeof(ResourceType).GetConstructors(Declared)).ToHashSet();

        foreach (var type in types.Where(type => !IsRegistry(type)))
        {
            foreach (var field in type.GetFields(Declared).Where(field => field.IsStatic && IsInstanceType(field.FieldType)))
            {
                violations.Add(H5Violation(type, $"static field {field.Name}", $"declares a {field.FieldType.Name} outside its registry", checkout));
            }

            foreach (var property in type.GetProperties(Declared).Where(property => property.GetMethod?.IsStatic == true && IsInstanceType(property.PropertyType)))
            {
                violations.Add(H5Violation(type, $"static property {property.Name}", $"declares a {property.PropertyType.Name} outside its registry", checkout));
            }

            foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                if (Creates(method, constructors) is { } created)
                {
                    violations.Add(H5Violation(type, $"method {method.Name}", $"creates a {created.Name} outside its registry", checkout));
                }
            }
        }

        return violations;
    }

    /// <summary>H6: a type deriving from <see cref="OwnershipResolver"/> that the allow list does not name.</summary>
    public static IReadOnlyList<Violation> H6(IEnumerable<Type> types, IReadOnlyList<string> allowed, string checkout) =>
        types.Where(type => type != typeof(OwnershipResolver) && typeof(OwnershipResolver).IsAssignableFrom(type) && !allowed.Contains(type.FullName!))
            .Select(type => new Violation(
                "H6", "core-contracts", null, $"type {type.FullName}", "derives from OwnershipResolver and is not in the contract's H6 table",
                RuleCatalog.ContractPath, checkout))
            .ToList();

    /// <summary>H7: a public member of a visible type whose signature, generic arguments included, uses a denied type.</summary>
    public static IReadOnlyList<Violation> H7(IEnumerable<Type> types, string checkout)
    {
        var violations = new List<Violation>();
        foreach (var type in types.Where(type => type.IsVisible))
        {
            // Accessors are covered by their property or event.
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(member => member is not MethodInfo { IsSpecialName: true }))
            {
                foreach (var denied in Signature(member).SelectMany(Expand).Where(IsDenied).Distinct())
                {
                    violations.Add(new Violation(
                        "H7", "core-contracts", null, $"{type.FullName}::{member.Name}", $"exposes {denied.FullName ?? denied.Name}", RuleCatalog.ContractPath, checkout));
                }
            }
        }

        return violations;
    }

    private static Violation H5Violation(Type type, string member, string what, string checkout) =>
        new("H5", "core-contracts", null, $"{type.FullName}::{member}", what, RuleCatalog.ContractPath, checkout);

    private static bool IsRegistry(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current == typeof(ErrorCodes) || current == typeof(ResourceTypes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInstanceType(Type type) => type == typeof(ErrorCode) || type == typeof(ResourceType);

    /// <summary>Finds a <c>newobj</c> of a registry type's constructor in the method's IL.</summary>
    private static Type? Creates(MethodBase method, HashSet<ConstructorInfo> constructors)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return null;
        }

        for (var index = 0; index + 4 < il.Length; index++)
        {
            if (il[index] != 0x73)
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, index + 1);
            try
            {
                if (method.Module.ResolveMethod(token) is ConstructorInfo constructor && constructors.Contains(constructor))
                {
                    return constructor.DeclaringType;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException)
            {
                // Not a method token: the byte was part of another instruction's operand.
            }
        }

        return null;
    }

    private static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        FieldInfo field => [field.FieldType],
        PropertyInfo property => [property.PropertyType, .. property.GetIndexParameters().Select(parameter => parameter.ParameterType)],
        EventInfo @event => @event.EventHandlerType is { } handler ? [handler] : [],
        MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
        ConstructorInfo constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType),
        _ => [],
    };

    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
        {
            return Expand(type.GetElementType()!);
        }

        return type.IsGenericType ? [type, .. type.GetGenericArguments().SelectMany(Expand)] : [type];
    }

    private static bool IsDenied(Type type)
    {
        if (type.IsGenericParameter)
        {
            return false;
        }

        var name = type.IsGenericType ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] : type.Name;
        return type == typeof(IServiceProvider)
            || DeniedNamespaces.Any(denied => type.Namespace == denied || type.Namespace?.StartsWith(denied + ".", StringComparison.Ordinal) == true)
            || DeniedNameEndings.Any(ending => name.EndsWith(ending, StringComparison.Ordinal));
    }
}
