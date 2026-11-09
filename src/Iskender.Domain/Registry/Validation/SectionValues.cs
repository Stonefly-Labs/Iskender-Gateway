using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Registry;

/// <summary>
/// The validated, typed values of one section, section field, or array item, with every applicable default written in.
/// A value is a <see cref="string"/>, <see cref="long"/>, <see cref="decimal"/>, <see cref="bool"/>,
/// <see cref="TimeSpan"/>, <see cref="Uri"/>, <see cref="SecretReference"/>, a nested <see cref="SectionValues"/>, or an
/// array of those. An absent optional field with no default has no entry.
/// </summary>
internal sealed class SectionValues(IReadOnlyList<FieldSchema> fields, IReadOnlyDictionary<string, object> values)
{
    public IReadOnlyList<FieldSchema> Fields { get; } = fields;

    public IReadOnlyDictionary<string, object> Values { get; } = values;
}
