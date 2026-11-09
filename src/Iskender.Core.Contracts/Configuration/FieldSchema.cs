using System.Collections.Immutable;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// One field of a section schema (FR-007). Built with <see cref="Create"/> and the <c>With</c> methods, each of which
/// returns a new instance. Nothing is checked until the registry is built (FR-008).
/// </summary>
[ContractVersion("core-contracts", 2)]
public sealed class FieldSchema
{
    private FieldSchema(string name, ConfigurationFieldKind kind)
    {
        Name = name;
        Kind = kind;
    }

    private FieldSchema(FieldSchema other)
    {
        Name = other.Name;
        Kind = other.Kind;
        IsRequired = other.IsRequired;
        Default = other.Default;
        IsDevelopmentOnly = other.IsDevelopmentOnly;
        Constraints = other.Constraints;
        Fields = other.Fields;
        Items = other.Items;
    }

    /// <summary>1 to 64 characters of <c>a-zA-Z0-9</c>, starting with <c>a-z</c>.</summary>
    public string Name { get; }

    /// <summary>The field's type.</summary>
    public ConfigurationFieldKind Kind { get; }

    /// <summary>Whether a value must be supplied. A required field has no default, and a section field cannot be required.</summary>
    public bool IsRequired { get; private init; }

    /// <summary>
    /// The default: a <see cref="string"/>, <see cref="long"/>, <see cref="decimal"/>, <see cref="bool"/>,
    /// <see cref="TimeSpan"/>, or <see cref="System.Uri"/> matching <see cref="Kind"/>, or null when there is none.
    /// </summary>
    public object? Default { get; private init; }

    /// <summary>Whether a value other than the default is allowed only in the <c>development</c> environment.</summary>
    public bool IsDevelopmentOnly { get; private init; }

    /// <summary>The field's constraints.</summary>
    public FieldConstraints Constraints { get; private init; } = FieldConstraints.None;

    /// <summary>The declared fields of a section field. Empty for every other type.</summary>
    public IReadOnlyList<FieldSchema> Fields { get; private init; } = ImmutableArray<FieldSchema>.Empty;

    /// <summary>The item type of an array field, which is not itself an array. Null for every other type.</summary>
    public FieldSchema? Items { get; private init; }

    /// <summary>Starts a field declaration.</summary>
    public static FieldSchema Create(string name, ConfigurationFieldKind kind) => new(name, kind);

    public FieldSchema Required() => new(this) { IsRequired = true };

    public FieldSchema DevelopmentOnly() => new(this) { IsDevelopmentOnly = true };

    public FieldSchema WithDefault(string value) => new(this) { Default = value };

    public FieldSchema WithDefault(long value) => new(this) { Default = value };

    public FieldSchema WithDefault(decimal value) => new(this) { Default = value };

    public FieldSchema WithDefault(bool value) => new(this) { Default = value };

    public FieldSchema WithDefault(TimeSpan value) => new(this) { Default = value };

    public FieldSchema WithDefault(Uri value) => new(this) { Default = value };

    public FieldSchema WithConstraints(FieldConstraints constraints) => new(this) { Constraints = constraints ?? FieldConstraints.None };

    /// <summary>Appends declared fields to a section field.</summary>
    public FieldSchema WithFields(params FieldSchema[] fields) =>
        new(this) { Fields = ((ImmutableArray<FieldSchema>)Fields).AddRange(fields ?? []) };

    public FieldSchema WithItems(FieldSchema items) => new(this) { Items = items };
}
