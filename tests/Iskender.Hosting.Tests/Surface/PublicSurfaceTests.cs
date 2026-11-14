using System.Reflection;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Hosting.Tests.Surface;

/// <summary>
/// The public surface of <c>Iskender.Hosting</c> names no <c>Iskender.Domain</c> type (FR-018), and every public type
/// carries its <c>hosting</c> version tag (FR-017).
/// </summary>
public class PublicSurfaceTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly Hosting = typeof(IskenderOptions).Assembly;

    [Fact]
    public void NoPublicTypeOrMemberNamesADomainType()
    {
        var offenders = Hosting.GetExportedTypes()
            .SelectMany(type => SurfaceOf(type).SelectMany(entry => Expand(entry.Type).Select(named => (entry.Where, Named: named))))
            .Where(entry => entry.Named.Assembly.GetName().Name == "Iskender.Domain")
            .Select(entry => $"{entry.Where}: {entry.Named.FullName}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryPublicTypeCarriesItsHostingTag()
    {
        var untagged = Hosting.GetExportedTypes()
            .Where(type => type.GetCustomAttribute<ContractVersionAttribute>() is not { ContractId: "hosting", Since: 1 })
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(untagged);
    }

    [Fact]
    public void ThePublicTypesAreTheSurfaceVersionOneNames()
    {
        Assert.Equal(
            ["Iskender.Hosting.IskenderBuilder", "Iskender.Hosting.IskenderConfigurationDocument", "Iskender.Hosting.IskenderDocumentFormat", "Iskender.Hosting.IskenderEndpointRouteBuilderExtensions", "Iskender.Hosting.IskenderOptions", "Iskender.Hosting.IskenderServiceCollectionExtensions"],
            Hosting.GetExportedTypes().Select(type => type.FullName!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheScanFindsATypeInAParameterAGenericArgumentAndAnAttribute()
    {
        var found = SurfaceOf(typeof(Probe)).SelectMany(entry => Expand(entry.Type)).ToHashSet();

        Assert.Contains(typeof(Uri), found);
        Assert.Contains(typeof(Version), found);
        Assert.Contains(typeof(ObsoleteAttribute), found);
        Assert.Contains(typeof(TimeZoneInfo), found);
    }

    /// <summary>Every type a public type names: its base, interfaces, attributes, and each public member's signature and attributes.</summary>
    private static IEnumerable<(string Where, Type Type)> SurfaceOf(Type type)
    {
        var name = type.FullName!;
        yield return (name, type);
        if (type.BaseType is { } baseType)
        {
            yield return (name, baseType);
        }

        foreach (var contract in type.GetInterfaces())
        {
            yield return (name, contract);
        }

        foreach (var attribute in type.GetCustomAttributesData())
        {
            yield return (name, attribute.AttributeType);
        }

        foreach (var member in type.GetMembers(Declared))
        {
            var where = $"{name}.{member.Name}";
            foreach (var attribute in member.GetCustomAttributesData())
            {
                yield return (where, attribute.AttributeType);
            }

            foreach (var named in Signature(member))
            {
                yield return (where, named);
            }
        }
    }

    private static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        MethodBase method => method.GetParameters().Select(parameter => parameter.ParameterType)
            .Concat(method.GetParameters().SelectMany(parameter => parameter.GetCustomAttributesData().Select(attribute => attribute.AttributeType)))
            .Concat(method is MethodInfo info ? [info.ReturnType] : [])
            .Concat(method.IsGenericMethodDefinition ? method.GetGenericArguments().SelectMany(argument => argument.GetGenericParameterConstraints()) : []),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        EventInfo @event => [@event.EventHandlerType!],
        Type nested => [nested],
        _ => [],
    };

    /// <summary>A type and everything it is built from: element types and generic arguments, recursively.</summary>
    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var inner in Expand(type.GetElementType()!))
            {
                yield return inner;
            }

            yield break;
        }

        if (type.IsGenericParameter)
        {
            yield break;
        }

        yield return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
        {
            foreach (var inner in Expand(argument))
            {
                yield return inner;
            }
        }
    }

    /// <summary>A stand-in type for checking that the scan reaches parameters, generic arguments, and attributes.</summary>
    public sealed class Probe
    {
        [Obsolete("probe")]
        public static IReadOnlyList<Version?> Find(Uri[] where, ref TimeZoneInfo zone) => [];
    }
}
