using System.Buffers;
using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Registry;

/// <summary>
/// Writes the contract's canonical form, <c>{"form":1,"iskender":{...},"schemas":{...}}</c>: keys sorted by ordinal
/// UTF-16 comparison at every level, defaults written in, every section written as an object, and absent optional
/// values omitted (research R9). The environment name, source names, and layer split are not part of it.
/// </summary>
internal static class CanonicalWriter
{
    public const int Form = 1;

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = false,
    };

    public static byte[] Write(IReadOnlyList<(SectionSchema Schema, SectionValues Values)> sections)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output, Options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("form", Form);
            writer.WritePropertyName("iskender");
            writer.WriteStartObject();
            foreach (var (schema, values) in sections.OrderBy(section => section.Schema.Namespace, StringComparer.Ordinal))
            {
                writer.WritePropertyName(schema.Namespace);
                WriteSection(writer, values);
            }

            writer.WriteEndObject();
            writer.WritePropertyName("schemas");
            writer.WriteStartObject();
            foreach (var (schema, _) in sections.OrderBy(section => section.Schema.Namespace, StringComparer.Ordinal))
            {
                writer.WriteNumber(schema.Namespace, schema.Version);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return output.WrittenSpan.ToArray();
    }

    private static void WriteSection(Utf8JsonWriter writer, SectionValues section)
    {
        writer.WriteStartObject();
        foreach (var key in section.Values.Keys.Order(StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            WriteValue(writer, section.Values[key]);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string text:
                writer.WriteStringValue(text);
                break;
            case long integer:
                writer.WriteNumberValue(integer);
                break;
            case decimal number:
                writer.WriteRawValue(FieldRules.Canonical(number), skipInputValidation: true);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case TimeSpan duration:
                writer.WriteNumberValue(duration.Ticks / TimeSpan.TicksPerMillisecond);
                break;
            case Uri uri:
                writer.WriteStringValue(uri.OriginalString);
                break;
            case SecretReference reference:
                writer.WriteStartObject();
                writer.WriteString("secretRef", reference.Name);
                writer.WriteEndObject();
                break;
            case SectionValues section:
                WriteSection(writer, section);
                break;
            case ImmutableArray<object> items:
                writer.WriteStartArray();
                foreach (var item in items)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new InvalidOperationException("A validated value has a type the canonical form does not define.");
        }
    }
}
