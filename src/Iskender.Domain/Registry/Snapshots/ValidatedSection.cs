using System.Collections.Immutable;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>The only section implementation: typed getters over validated values, failing on an undeclared name or the wrong type.</summary>
internal sealed class ValidatedSection(string @namespace, int schemaVersion, string path, SectionValues values)
    : ConfigurationSection(@namespace, schemaVersion, path)
{
    public override string? GetString(string name) => Scalar<string>(name, ConfigurationFieldKind.Text, ConfigurationFieldKind.Enumeration);

    public override long? GetInt64(string name) => (long?)Scalar<object>(name, ConfigurationFieldKind.WholeNumber);

    public override decimal? GetDecimal(string name) => (decimal?)Scalar<object>(name, ConfigurationFieldKind.Number);

    public override bool? GetBoolean(string name) => (bool?)Scalar<object>(name, ConfigurationFieldKind.Boolean);

    public override TimeSpan? GetDuration(string name) => (TimeSpan?)Scalar<object>(name, ConfigurationFieldKind.Duration);

    public override Uri? GetUri(string name) => Scalar<Uri>(name, ConfigurationFieldKind.Uri);

    public override SecretReference? GetSecretReference(string name) => Scalar<SecretReference>(name, ConfigurationFieldKind.SecretReference);

    public override ConfigurationSection GetSection(string name)
    {
        var field = Field(name, ConfigurationFieldKind.Section);
        return new ValidatedSection(Namespace, SchemaVersion, $"{Path}.{field.Name}", (SectionValues)values.Values[field.Name]);
    }

    public override IReadOnlyList<ConfigurationSection>? GetSections(string name)
    {
        var items = Items(name, ConfigurationFieldKind.Section);
        return items?.Select((item, index) => (ConfigurationSection)new ValidatedSection(Namespace, SchemaVersion, $"{Path}.{name}[{index}]", (SectionValues)item))
            .ToImmutableArray();
    }

    public override IReadOnlyList<string>? GetStrings(string name) => Items<string>(name, ConfigurationFieldKind.Text, ConfigurationFieldKind.Enumeration);

    public override IReadOnlyList<long>? GetInt64s(string name) => Items<long>(name, ConfigurationFieldKind.WholeNumber);

    public override IReadOnlyList<decimal>? GetDecimals(string name) => Items<decimal>(name, ConfigurationFieldKind.Number);

    public override IReadOnlyList<bool>? GetBooleans(string name) => Items<bool>(name, ConfigurationFieldKind.Boolean);

    public override IReadOnlyList<TimeSpan>? GetDurations(string name) => Items<TimeSpan>(name, ConfigurationFieldKind.Duration);

    public override IReadOnlyList<Uri>? GetUris(string name) => Items<Uri>(name, ConfigurationFieldKind.Uri);

    public override IReadOnlyList<SecretReference>? GetSecretReferences(string name) => Items<SecretReference>(name, ConfigurationFieldKind.SecretReference);

    private T? Scalar<T>(string name, params ConfigurationFieldKind[] kinds)
        where T : class
    {
        var field = Field(name, kinds);
        return values.Values.TryGetValue(field.Name, out var value) ? (T)value : null;
    }

    private ImmutableArray<T>? Items<T>(string name, params ConfigurationFieldKind[] kinds) =>
        Items(name, kinds) is { } items ? items.Cast<T>().ToImmutableArray() : null;

    private ImmutableArray<object>? Items(string name, params ConfigurationFieldKind[] kinds)
    {
        var field = Field(name, ConfigurationFieldKind.Array);
        if (!kinds.Contains(field.Items!.Kind))
        {
            throw Misuse(name, "the getter does not match the declared item type");
        }

        return values.Values.TryGetValue(field.Name, out var value) ? (ImmutableArray<object>)value : null;
    }

    private FieldSchema Field(string name, params ConfigurationFieldKind[] kinds)
    {
        var field = values.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? throw Misuse(name, "the field is not declared");
        return kinds.Contains(field.Kind) ? field : throw Misuse(name, "the getter does not match the declared type");
    }

    private GatewayException Misuse(string? name, string misuse) =>
        Faults.Create(ErrorCodes.ConfigSectionInvalidAccess, ("namespace", Namespace), ("field", $"{Path}.{name}"), ("misuse", misuse));
}
