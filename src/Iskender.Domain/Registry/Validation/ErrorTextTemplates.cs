using System.Globalization;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>A field error's problem and fix.</summary>
internal sealed record ErrorText(string Problem, string Fix);

/// <summary>
/// One problem and fix template per code, filled only with the path, the schema's constraints, the layer, and declared
/// names, never with a configuration value (FR-020, research R13).
/// </summary>
internal static class ErrorTextTemplates
{
    public static ErrorText Document(ErrorCode code) => code.Id switch
    {
        "config.document.too_large" => new(
            "the document passes a limit: 1 MiB in all, 32 levels of nesting, 10,000 nodes, or 64 KiB in one string",
            "make the document smaller or flatter."),
        "config.document.duplicate_key" => new("a key appears twice in one object", "remove or rename the repeated key."),
        "config.document.unsupported_construct" => new(
            "the document uses a YAML anchor, alias, tag, merge key, directive, or more than one document",
            "write every value out in full, in a single document, without those constructs."),
        _ => new("the document is not well-formed JSON or YAML, or is not clean UTF-8 without a byte order mark or control characters",
            "correct the document where reading stopped."),
    };

    public static ErrorText EnvironmentInvalid() => new(
        "the environment name is not 1 to 32 characters of a-z, 0-9, and -",
        "set a valid environment name, such as production, or none.");

    public static ErrorText NamespaceUnknown(IEnumerable<string> registered)
    {
        var names = registered.Order(StringComparer.Ordinal).Take(10).ToList();
        return new("no section is registered for this namespace",
            names.Count == 0 ? "remove the section; no sections are registered." : $"use a registered section: {string.Join(", ", names)}.");
    }

    public static ErrorText FieldUnknown(string ns, string name, IEnumerable<string> siblings)
    {
        var (closest, listed) = NameSuggester.Suggest(name, siblings);
        var fix = closest is not null ? $"did you mean {closest}?"
            : listed.Count == 0 ? "remove the field; this object declares no fields."
            : $"use a declared field: {string.Join(", ", listed)}.";
        return new($"the field is not declared by section {ns}", fix);
    }

    public static ErrorText Required(string path) => new("a required field is missing", $"set {path}.");

    public static ErrorText TypeMismatch(FieldSchema field) =>
        new($"must be {Kind(field)}", $"set {Kind(field)}.");

    public static ErrorText PlaceholderInPlainField(FieldSchema field) =>
        new($"must be {Kind(field)}, not a secretRef placeholder", $"set {Kind(field)}; only a secret field takes a secretRef placeholder.");

    public static ErrorText OutOfRange(FieldSchema field)
    {
        var c = field.Constraints;
        return field.Kind switch
        {
            ConfigurationFieldKind.WholeNumber => Range("an integer", Text(c.MinimumInteger ?? long.MinValue), Text(c.MaximumInteger ?? long.MaxValue)),
            ConfigurationFieldKind.Number when c.MinimumNumber is not null || c.MaximumNumber is not null =>
                Range("a number", c.MinimumNumber is { } min ? Text(min) : null, c.MaximumNumber is { } max ? Text(max) : null, exact: true),
            ConfigurationFieldKind.Number => new("must be a number that a decimal holds exactly",
                "use a number within decimal's range with at most 28 significant digits."),
            ConfigurationFieldKind.Duration => Range("a duration", Text(c.MinimumDuration ?? TimeSpan.Zero), Text(c.MaximumDuration ?? ScalarSyntax.MaxDuration)),
            ConfigurationFieldKind.Text => new(
                $"must be {Bounds(c.MinimumLength, c.MaximumLength)} characters long",
                $"use {Bounds(c.MinimumLength, c.MaximumLength)} characters."),
            ConfigurationFieldKind.Array => new($"must have {Bounds(c.MinimumItems, c.MaximumItems)} items", $"use {Bounds(c.MinimumItems, c.MaximumItems)} items."),
            _ => new($"must be {Kind(field)} within its allowed range", $"set {Kind(field)} within its allowed range."),
        };
    }

    public static ErrorText InvalidFormat(FieldSchema field, bool scheme = false)
    {
        var c = field.Constraints;
        return field.Kind switch
        {
            ConfigurationFieldKind.Enumeration => new($"must be one of: {string.Join(", ", c.Members)}", $"set one of: {string.Join(", ", c.Members)}."),
            ConfigurationFieldKind.Duration => new("must be a non-negative whole number with no leading zero and a unit, ms, s, m, h, or d",
                "write the duration as, for example, 30s."),
            ConfigurationFieldKind.Uri when scheme => new($"must be a URI with scheme {string.Join(" or ", c.AllowedSchemes)}",
                $"use scheme {string.Join(" or ", c.AllowedSchemes)}."),
            ConfigurationFieldKind.Uri => new("must be an absolute URI", "set an absolute URI, such as https://example.com/."),
            _ => new($"may hold only {c.AllowedCharacters}", $"use only {c.AllowedCharacters}."),
        };
    }

    public static ErrorText SecretPlaintext() => new(
        "a secret field holds a plain value instead of a secretRef placeholder",
        "replace it with a secretRef placeholder, such as { secretRef: <name> }, or set it through a variable ending in __SECRETREF.");

    public static ErrorText SecretReferenceMalformed() => new(
        "the placeholder must be an object with one member, secretRef, holding a name of 1 to 256 characters of a-z, A-Z, 0-9, ., _, -, and /, with no leading or trailing /, no empty segment, and no .. segment",
        "write it as { secretRef: <name> } with a valid reference name.");

    public static ErrorText UnsafeForEnvironment() => new(
        "a development-only value is set outside the development environment",
        "remove the value, or run in the development environment.");

    public static ErrorText OverlayConflict(string first, string second) => new(
        $"the environment variables {first} and {second} both set this field",
        "set this field with one variable only.");

    public static ErrorText VariableNotScalar(FieldSchema field) => new(
        $"must be {Kind(field)}, which an environment variable cannot set",
        "set this field in a document; a variable sets only a scalar field.");

    public static ErrorText VariableNamesNoPath() => new(
        "the variable names no declared path",
        "name a declared field, as ISKENDER__<SECTION>__<FIELD>, with no empty segment.");

    public static ErrorText VariableNamesSection() => new(
        "must be an object of the section's fields, which an environment variable cannot set",
        "name a scalar field inside the section, as ISKENDER__<SECTION>__<FIELD>.");

    private static ErrorText Range(string what, string? minimum, string? maximum, bool exact = false)
    {
        var bounds = (minimum, maximum) switch
        {
            ({ } min, { } max) => $"from {min} to {max}",
            ({ } min, null) => $"of at least {min}",
            (null, { } max) => $"of at most {max}",
            _ => "in range",
        };
        var problem = $"must be {what} {bounds}" + (exact ? " that a decimal holds exactly" : string.Empty);
        return new(problem, $"set a value {bounds}.");
    }

    private static string Bounds(int? minimum, int? maximum) => (minimum, maximum) switch
    {
        ({ } min, { } max) => $"{min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}",
        ({ } min, null) => $"at least {min.ToString(CultureInfo.InvariantCulture)}",
        (null, { } max) => $"at most {max.ToString(CultureInfo.InvariantCulture)}",
        _ => "an allowed number of",
    };

    private static string Kind(FieldSchema field) => field.Kind switch
    {
        ConfigurationFieldKind.Text => "a string",
        ConfigurationFieldKind.WholeNumber => "an integer",
        ConfigurationFieldKind.Number => "a number",
        ConfigurationFieldKind.Boolean => "true or false",
        ConfigurationFieldKind.Enumeration => $"one of: {string.Join(", ", field.Constraints.Members)}",
        ConfigurationFieldKind.Duration => "a duration, such as 30s",
        ConfigurationFieldKind.Uri => "an absolute URI",
        ConfigurationFieldKind.Section => "an object",
        ConfigurationFieldKind.Array => "an array",
        _ => "a secretRef placeholder",
    };

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(decimal value) => FieldRules.Canonical(value);

    /// <summary>A duration in the largest unit that divides it evenly, such as 5m or 1500ms.</summary>
    internal static string Text(TimeSpan value)
    {
        var ms = (long)value.TotalMilliseconds;
        (long Size, string Unit)[] units = [(86_400_000, "d"), (3_600_000, "h"), (60_000, "m"), (1_000, "s")];
        foreach (var (size, unit) in units)
        {
            if (ms != 0 && ms % size == 0)
            {
                return (ms / size).ToString(CultureInfo.InvariantCulture) + unit;
            }
        }

        return ms.ToString(CultureInfo.InvariantCulture) + "ms";
    }
}
