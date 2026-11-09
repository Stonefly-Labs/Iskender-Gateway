using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Registry;

/// <summary>
/// Writes the contract's canonical form, <c>{"form":2,"iskender":{...},"schemas":{...}}</c>: keys sorted by ordinal
/// UTF-16 comparison at every level, defaults written in, every section written as an object, and absent optional
/// values omitted (research R9). The environment name, source names, and layer split are not part of it. A string is
/// escaped only where JSON requires it, so its bytes never depend on an encoder's view of Unicode (contract, Canonical
/// form rule 4).
/// </summary>
internal static class CanonicalWriter
{
    public const int Form = 2;

    // Keys are declared names, which are ASCII letters, digits, and '-', so the writer's encoder never changes them.
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = false,
        SkipValidation = false,
    };

    // A validated string holds no lone surrogate, so encoding one is a defect, never a silent U+FFFD.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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
                WriteString(writer, text);
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
                WriteString(writer, uri.OriginalString);
                break;
            case SecretReference reference:
                writer.WriteStartObject();
                writer.WritePropertyName("secretRef");
                WriteString(writer, reference.Name);
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

    /// <summary>
    /// Writes a string with only what JSON requires escaped: <c>"</c> and <c>\</c> as <c>\"</c> and <c>\\</c>, tab, line
    /// feed, carriage return, backspace, and form feed as <c>\t</c>, <c>\n</c>, <c>\r</c>, <c>\b</c>, and <c>\f</c>, and any
    /// other code point below U+0020 as <c>\u00XX</c> in upper-case hex. Every other code point is raw UTF-8, whether it is
    /// supplementary, private-use, U+2028, or unassigned in this runtime's Unicode tables.
    /// </summary>
    private static void WriteString(Utf8JsonWriter writer, string text)
    {
        var json = new StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
        {
            _ = c switch
            {
                '"' => json.Append("\\\""),
                '\\' => json.Append("\\\\"),
                '\t' => json.Append("\\t"),
                '\n' => json.Append("\\n"),
                '\r' => json.Append("\\r"),
                '\b' => json.Append("\\b"),
                '\f' => json.Append("\\f"),
                < ' ' => json.Append("\\u00").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture)),
                _ => json.Append(c),
            };
        }

        writer.WriteRawValue(Utf8.GetBytes(json.Append('"').ToString()), skipInputValidation: true);
    }
}
