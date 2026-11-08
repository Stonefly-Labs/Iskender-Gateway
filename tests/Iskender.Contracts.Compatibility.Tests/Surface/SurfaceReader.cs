using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Contracts.Compatibility.Tests.Surface;

/// <summary>
/// Reads an assembly's public surface as canonical lines (research R2): one per public type, and one per public or
/// protected member of a type visible outside the assembly. Names come from metadata, never from <c>ToString()</c>, and
/// lines are sorted ordinally, so the output is the same on every platform.
/// </summary>
public sealed class SurfaceReader
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly NullabilityInfoContext _nullability = new();
    private readonly string? _prefix;

    private SurfaceReader(string? namespacePrefix)
    {
        _prefix = namespacePrefix;
    }

    /// <summary>
    /// Reads the whole assembly, or only the types in <paramref name="namespacePrefix"/> and its child namespaces, with
    /// the prefix and its dot stripped from every type name.
    /// </summary>
    public static IReadOnlyList<string> Read(Assembly assembly, string? namespacePrefix = null) =>
        new SurfaceReader(namespacePrefix).ReadLines(assembly).Select(line => line.ToString()).Order(StringComparer.Ordinal).ToList();

    private IEnumerable<SurfaceLine> ReadLines(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(IsSurfaceType).Where(InScope))
        {
            yield return TypeLine(type);

            foreach (var member in Members(type))
            {
                yield return member;
            }
        }
    }

    private bool InScope(Type type)
    {
        if (_prefix is null)
        {
            return true;
        }

        var ns = type.Namespace ?? string.Empty;
        return ns == _prefix || ns.StartsWith(_prefix + ".", StringComparison.Ordinal);
    }

    private static bool IsSurfaceType(Type type) =>
        !type.Name.Contains('<', StringComparison.Ordinal)
        && (type.IsNested ? IsSurfaceType(type.DeclaringType!) && (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem) : type.IsPublic);

    private SurfaceLine TypeLine(Type type)
    {
        var attributes = new List<KeyValuePair<string, string?>> { Value("access", type.IsNested && !type.IsNestedPublic ? "protected" : "public") };

        if (type.IsClass && !IsDelegate(type))
        {
            if (type.IsAbstract && type.IsSealed)
            {
                attributes.Add(Flag("static"));
            }
            else if (type.IsAbstract)
            {
                attributes.Add(Flag("abstract"));
            }
            else if (type.IsSealed)
            {
                attributes.Add(Flag("sealed"));
            }
        }

        attributes.Add(Value("kind", KindOf(type)));

        if (type.IsClass && !IsDelegate(type) && type.BaseType is { } baseType && baseType != typeof(object))
        {
            attributes.Add(Value("base", Name(baseType)));
        }

        // Interfaces a framework base type brings, such as System.Enum's, change with the runtime, so only the ones this
        // assembly's own types add are listed. Removing the base type itself is still S9.
        var inherited = FrameworkBase(type)?.GetInterfaces() ?? [];
        var interfaces = type.GetInterfaces().Where(IsVisibleType).Except(inherited).Select(Name).Order(StringComparer.Ordinal).ToList();
        if (interfaces.Count > 0)
        {
            attributes.Add(Value("interfaces", string.Join(",", interfaces)));
        }

        if (type.IsEnum)
        {
            attributes.Add(Value("underlying", Name(Enum.GetUnderlyingType(type))));
        }

        AddCommon(attributes, type, Tag(type));
        return new SurfaceLine("type", Name(type), attributes);
    }

    private IEnumerable<SurfaceLine> Members(Type type)
    {
        var typeTag = Tag(type);

        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var attributes = new List<KeyValuePair<string, string?>>
                {
                    Value("value", Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
                };
                AddCommon(attributes, field, Tag(field) ?? typeTag);
                yield return new SurfaceLine("enum-value", $"{Name(type)}::{field.Name}", attributes);
            }

            yield break;
        }

        // A delegate's signature is its Invoke method. Its constructor and asynchronous pair are runtime plumbing.
        if (IsDelegate(type))
        {
            var invoke = type.GetMethod("Invoke")!;
            var attributes = new List<KeyValuePair<string, string?>> { Access(invoke) };
            attributes.Add(Value("returns", Annotated(_nullability.Create(invoke.ReturnParameter), write: false)));
            AddParameters(attributes, invoke.GetParameters());
            AddCommon(attributes, invoke, Tag(invoke) ?? typeTag);
            yield return new SurfaceLine("method", $"{Name(type)}::Invoke({Signature(invoke.GetParameters())})", attributes);
            yield break;
        }

        foreach (var constructor in type.GetConstructors(Declared).Where(IsVisibleMember))
        {
            var attributes = new List<KeyValuePair<string, string?>> { Access(constructor) };
            AddParameters(attributes, constructor.GetParameters());
            AddCommon(attributes, constructor, Tag(constructor) ?? typeTag);
            yield return new SurfaceLine("ctor", $"{Name(type)}::.ctor({Signature(constructor.GetParameters())})", attributes);
        }

        var accessors = new HashSet<MethodInfo>();
        foreach (var property in type.GetProperties(Declared))
        {
            var getter = property.GetMethod is { } get && IsVisibleMember(get) ? get : null;
            var setter = property.SetMethod is { } set && IsVisibleMember(set) ? set : null;
            accessors.UnionWith(new[] { property.GetMethod, property.SetMethod }.OfType<MethodInfo>());
            var main = getter ?? setter;
            if (main is null || IsPlainOverride(main))
            {
                continue;
            }

            var attributes = new List<KeyValuePair<string, string?>> { Access(getter is not null && setter is not null && setter.IsPublic && !getter.IsPublic ? setter : main) };
            AddModifiers(attributes, main, type);
            attributes.Add(Value("type", Annotated(_nullability.Create(property), write: false)));
            var indexParameters = property.GetIndexParameters();
            if (indexParameters.Length > 0)
            {
                AddParameters(attributes, indexParameters);
            }

            if (getter is not null)
            {
                attributes.Add(Value("get", AccessName(getter)));
            }

            if (setter is not null)
            {
                var init = setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
                attributes.Add(Value(init ? "init" : "set", AccessName(setter)));
            }

            AddCommon(attributes, property, Tag(property) ?? typeTag);
            var key = indexParameters.Length > 0 ? $"{property.Name}({Signature(indexParameters)})" : property.Name;
            yield return new SurfaceLine("property", $"{Name(type)}::{key}", attributes);
        }

        foreach (var @event in type.GetEvents(Declared))
        {
            accessors.UnionWith(new[] { @event.AddMethod, @event.RemoveMethod, @event.RaiseMethod }.OfType<MethodInfo>());
            if (@event.AddMethod is not { } add || !IsVisibleMember(add) || IsPlainOverride(add))
            {
                continue;
            }

            var attributes = new List<KeyValuePair<string, string?>> { Access(add) };
            AddModifiers(attributes, add, type);
            attributes.Add(Value("type", Annotated(_nullability.Create(@event), write: false)));
            AddCommon(attributes, @event, Tag(@event) ?? typeTag);
            yield return new SurfaceLine("event", $"{Name(type)}::{@event.Name}", attributes);
        }

        foreach (var field in type.GetFields(Declared).Where(field => (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly) && !field.IsSpecialName))
        {
            var attributes = new List<KeyValuePair<string, string?>> { Value("access", field.IsPublic ? "public" : "protected") };
            if (field.IsLiteral)
            {
                attributes.Add(Flag("const"));
            }
            else
            {
                if (field.IsStatic)
                {
                    attributes.Add(Flag("static"));
                }

                if (field.IsInitOnly)
                {
                    attributes.Add(Flag("readonly"));
                }
            }

            attributes.Add(Value("type", Annotated(_nullability.Create(field), write: false)));
            if (field.IsLiteral)
            {
                attributes.Add(Value("value", Constant(field.GetRawConstantValue())));
            }

            AddCommon(attributes, field, Tag(field) ?? typeTag);
            yield return new SurfaceLine("field", $"{Name(type)}::{field.Name}", attributes);
        }

        foreach (var method in type.GetMethods(Declared).Where(method => IsVisibleMember(method) && !accessors.Contains(method) && !IsPlainOverride(method)))
        {
            var attributes = new List<KeyValuePair<string, string?>> { Access(method) };
            AddModifiers(attributes, method, type);
            attributes.Add(Value("returns", Annotated(_nullability.Create(method.ReturnParameter), write: false)));
            AddParameters(attributes, method.GetParameters());
            AddCommon(attributes, method, Tag(method) ?? typeTag);
            var name = method.IsGenericMethodDefinition ? $"{method.Name}``{method.GetGenericArguments().Length}" : method.Name;
            yield return new SurfaceLine("method", $"{Name(type)}::{name}({Signature(method.GetParameters())})", attributes);
        }
    }

    private void AddParameters(List<KeyValuePair<string, string?>> attributes, ParameterInfo[] parameters)
    {
        if (parameters.Length == 0)
        {
            return;
        }

        attributes.Add(Value("params", string.Join(",", parameters.Select(Parameter))));
    }

    private string Parameter(ParameterInfo parameter)
    {
        var passing = parameter.ParameterType.IsByRef ? (parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ") : string.Empty;
        var info = _nullability.Create(parameter);
        var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        var annotated = Annotated(info, write: !parameter.IsOut);

        // A plain type shows only its marker, as in the data model's example. A constructed generic or an array shows its
        // whole annotated type, so that a generic argument's annotation is compared too (S5).
        var suffix = type.IsArray || (type.IsGenericType && Nullable.GetUnderlyingType(type) is null) ? ":" + annotated : Marker(annotated);
        var text = passing + parameter.Name + suffix;
        return parameter.HasDefaultValue ? text + "=" + Constant(parameter.DefaultValue) : text;
    }

    private static string Marker(string annotated) => annotated.Length > 0 && annotated[^1] is '!' or '?' or '~' ? annotated[^1..] : string.Empty;

    private static void AddModifiers(List<KeyValuePair<string, string?>> attributes, MethodBase method, Type declaringType)
    {
        if (method.IsStatic)
        {
            attributes.Add(Flag("static"));
        }

        if (method.IsAbstract)
        {
            attributes.Add(Flag("abstract"));
        }
        else if (method.IsVirtual && !method.IsFinal && !(declaringType.IsSealed && !declaringType.IsInterface))
        {
            attributes.Add(Flag("virtual"));
        }
        else if (method.IsVirtual && method.IsFinal && method is MethodInfo info && info.GetBaseDefinition() != info)
        {
            attributes.Add(Flag("sealed"));
        }
    }

    private static void AddCommon(List<KeyValuePair<string, string?>> attributes, MemberInfo member, ContractVersionAttribute? tag)
    {
        if (member.GetCustomAttribute<ObsoleteAttribute>(inherit: false) is { } obsolete)
        {
            attributes.Add(Value("obsolete", obsolete.Message ?? string.Empty));
        }

        if (tag is not null)
        {
            attributes.Add(Value("since", $"{tag.ContractId}@{tag.Since.ToString(CultureInfo.InvariantCulture)}"));
        }
    }

    /// <summary>The nearest base type from another assembly, or null.</summary>
    private static Type? FrameworkBase(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Assembly != type.Assembly)
            {
                return current;
            }
        }

        return null;
    }

    private static ContractVersionAttribute? Tag(MemberInfo member) => member.GetCustomAttribute<ContractVersionAttribute>(inherit: false);

    private static bool IsVisibleMember(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsVisibleType(Type type) => type.IsPublic || (type.IsNestedPublic && IsVisibleType(type.DeclaringType!));

    /// <summary>An override that is not abstract adds nothing a caller can lose, so it is not surface.</summary>
    private static bool IsPlainOverride(MethodInfo method) =>
        !method.IsAbstract && method.IsVirtual && method.GetBaseDefinition() != method && !method.DeclaringType!.IsInterface;

    private static KeyValuePair<string, string?> Access(MethodBase method) => Value("access", AccessName(method));

    private static string AccessName(MethodBase method) => method.IsPublic ? "public" : "protected";

    private static bool IsDelegate(Type type) => typeof(Delegate).IsAssignableFrom(type.BaseType);

    private static string KindOf(Type type) =>
        type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : IsDelegate(type) ? "delegate" : "class";

    private static KeyValuePair<string, string?> Flag(string name) => new(name, null);

    private static KeyValuePair<string, string?> Value(string name, string value) => new(name, value);

    private string Signature(ParameterInfo[] parameters) => string.Join(",", parameters.Select(parameter => Name(parameter.ParameterType)));

    /// <summary>A type's metadata name, with generic arguments, and with the scope prefix stripped.</summary>
    private string Name(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsByRef)
        {
            return Name(type.GetElementType()!) + "&";
        }

        if (type.IsPointer)
        {
            return Name(type.GetElementType()!) + "*";
        }

        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            return Name(type.GetGenericTypeDefinition()) + "<" + string.Join(",", type.GetGenericArguments().Select(Name)) + ">";
        }

        var name = type.IsNested ? Name(type.DeclaringType!) + "+" + type.Name : type.Namespace is null ? type.Name : type.Namespace + "." + type.Name;
        return _prefix is not null && name.StartsWith(_prefix + ".", StringComparison.Ordinal) ? name[(_prefix.Length + 1)..] : name;
    }

    /// <summary>A type with its nullable annotations: <c>!</c> not null, <c>?</c> nullable, <c>~</c> oblivious.</summary>
    private string Annotated(NullabilityInfo info, bool write)
    {
        var type = info.Type;
        var state = write ? info.WriteState : info.ReadState;
        var marker = type.IsValueType ? string.Empty : state switch
        {
            NullabilityState.Nullable => "?",
            NullabilityState.NotNull => "!",
            _ => "~",
        };

        if (type.IsArray && info.ElementType is { } element)
        {
            return Annotated(element, write) + "[" + new string(',', type.GetArrayRank() - 1) + "]" + marker;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition && info.GenericTypeArguments.Length > 0)
        {
            return Name(type.GetGenericTypeDefinition()) + "<" + string.Join(",", info.GenericTypeArguments.Select(argument => Annotated(argument, write))) + ">" + marker;
        }

        return Name(type) + marker;
    }

    private static string Constant(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        bool flag => flag ? "true" : "false",
        char character => "'" + character + "'",
        Enum enumValue => Convert.ToInt64(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}
