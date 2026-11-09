using System.Collections.Frozen;
using System.Collections.Immutable;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>
/// Every section schema of a process, checked once when built (FR-008). A fault is a module's own bug, so it throws a
/// <see cref="GatewayException"/> in the <c>internal</c> category before any document is read (research R14).
/// </summary>
public sealed class ConfigurationSchemaRegistry
{
    private readonly FrozenDictionary<string, SectionSchema> _byNamespace;

    private ConfigurationSchemaRegistry(ImmutableArray<SectionSchema> schemas)
    {
        Schemas = schemas;
        _byNamespace = schemas.ToFrozenDictionary(schema => schema.Namespace, StringComparer.Ordinal);
    }

    /// <summary>The registered schemas, in ordinal namespace order.</summary>
    public IReadOnlyList<SectionSchema> Schemas { get; }

    /// <summary>
    /// Checks every schema and throws one exception for the first fault in namespace order:
    /// <c>config.schema.duplicate_namespace</c>, naming both owning modules in its diagnostics, or
    /// <c>config.schema.invalid</c>, naming the namespace, field, and broken rule.
    /// </summary>
    public static ConfigurationSchemaRegistry Create(IEnumerable<SectionSchema> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        var list = schemas.ToList();
        if (list.Any(schema => schema is null))
        {
            throw Invalid("(none)", null, "a schema is null");
        }

        var ordered = list.OrderBy(schema => schema.Namespace ?? string.Empty, StringComparer.Ordinal).ToImmutableArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var schema = ordered[index];
            if (index + 1 < ordered.Length && string.Equals(schema.Namespace, ordered[index + 1].Namespace, StringComparison.Ordinal))
            {
                throw Faults.Create(ErrorCodes.ConfigSchemaDuplicateNamespace,
                    ("namespace", schema.Namespace ?? string.Empty),
                    ("firstOwner", schema.OwnerModule ?? string.Empty),
                    ("secondOwner", ordered[index + 1].OwnerModule ?? string.Empty));
            }

            CheckSection(schema);
        }

        return new ConfigurationSchemaRegistry(ordered);
    }

    internal SectionSchema? Find(string @namespace) => _byNamespace.GetValueOrDefault(@namespace);

    internal static bool IsNamespace(string? value) =>
        value is { Length: >= 1 and <= 32 } && value[0] is >= 'a' and <= 'z'
            && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    internal static bool IsFieldName(string? value) =>
        value is { Length: >= 1 and <= 64 } && value[0] is >= 'a' and <= 'z'
            && value.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'));

    private static void CheckSection(SectionSchema schema)
    {
        var ns = schema.Namespace ?? string.Empty;
        if (!IsNamespace(schema.Namespace))
        {
            throw Invalid(ns, null, "the namespace is not 1 to 32 characters of a-z, 0-9, and -, starting with a letter");
        }

        if (schema.OwnerModule is not { Length: >= 1 and <= 64 })
        {
            throw Invalid(ns, null, "the owner module is not 1 to 64 characters");
        }

        if (schema.Version < 1)
        {
            throw Invalid(ns, null, "the version is below 1");
        }

        CheckFields(ns, ns, schema.Fields);
    }

    private static void CheckFields(string ns, string parent, IReadOnlyList<FieldSchema> fields)
    {
        var names = new HashSet<string>(FieldRules.IgnoreAsciiCase);
        foreach (var field in fields)
        {
            if (field is null)
            {
                throw Invalid(ns, parent, "a field is null");
            }

            var path = $"{parent}.{field.Name}";
            CheckName(ns, path, field.Name);
            if (!names.Add(field.Name))
            {
                throw Invalid(ns, path, "two sibling fields have the same name, or names that differ only in case");
            }

            CheckField(ns, path, field, isItem: false);
        }
    }

    private static void CheckName(string ns, string path, string? name)
    {
        if (!IsFieldName(name))
        {
            throw Invalid(ns, path, "the field name is not 1 to 64 characters of a-z, A-Z, and 0-9, starting with a-z");
        }

        if (FieldRules.IgnoreAsciiCase.Equals(name, "secretRef"))
        {
            throw Invalid(ns, path, "the name secretRef is reserved for placeholders");
        }
    }

    private static void CheckField(string ns, string path, FieldSchema field, bool isItem)
    {
        var kind = field.Kind;
        if (!Enum.IsDefined(kind))
        {
            throw Invalid(ns, path, "the field type is not defined");
        }

        if (isItem && (field.IsRequired || field.Default is not null || field.IsDevelopmentOnly))
        {
            throw Invalid(ns, path, "an array item cannot be required, development-only, or have a default");
        }

        if (field.IsRequired && field.Default is not null)
        {
            throw Invalid(ns, path, "a required field has a default");
        }

        if (field.IsRequired && field.IsDevelopmentOnly)
        {
            throw Invalid(ns, path, "a field is both required and development-only");
        }

        if (field.IsRequired && kind == ConfigurationFieldKind.Section)
        {
            throw Invalid(ns, path, "an object field cannot be required");
        }

        if (field.Default is not null && kind is ConfigurationFieldKind.Section or ConfigurationFieldKind.Array or ConfigurationFieldKind.SecretReference)
        {
            throw Invalid(ns, path, "an object, array, or secretRef field cannot have a default");
        }

        if (field.IsDevelopmentOnly && kind == ConfigurationFieldKind.SecretReference)
        {
            throw Invalid(ns, path, "a secretRef field cannot be development-only");
        }

        if (kind != ConfigurationFieldKind.Section && field.Fields.Count > 0)
        {
            throw Invalid(ns, path, "only an object field declares fields");
        }

        if (kind != ConfigurationFieldKind.Array && field.Items is not null)
        {
            throw Invalid(ns, path, "only an array field declares an item type");
        }

        CheckConstraints(ns, path, kind, field.Constraints);

        if (kind == ConfigurationFieldKind.Section)
        {
            CheckFields(ns, path, field.Fields);
        }

        if (kind == ConfigurationFieldKind.Array)
        {
            if (field.Items is not { } items)
            {
                throw Invalid(ns, path, "an array field declares no item type");
            }

            if (items.Kind == ConfigurationFieldKind.Array)
            {
                throw Invalid(ns, path, "an array of arrays is not allowed");
            }

            var itemPath = path + "[]";
            CheckName(ns, itemPath, items.Name);
            CheckField(ns, itemPath, items, isItem: true);
        }

        if (field.Default is { } value && !DefaultSatisfies(kind, value, field.Constraints))
        {
            throw Invalid(ns, path, "the default is not of the field's type, or breaks its own constraints");
        }
    }

    private static void CheckConstraints(string ns, string path, ConfigurationFieldKind kind, FieldConstraints? constraints)
    {
        if (constraints is null)
        {
            throw Invalid(ns, path, "the constraints are null");
        }

        void Applies(bool set, ConfigurationFieldKind to, string name)
        {
            if (set && kind != to)
            {
                throw Invalid(ns, path, $"the {name} constraint does not apply to this field type");
            }
        }

        Applies(constraints.MinimumInteger is not null || constraints.MaximumInteger is not null, ConfigurationFieldKind.WholeNumber, "integer range");
        Applies(constraints.MinimumNumber is not null || constraints.MaximumNumber is not null, ConfigurationFieldKind.Number, "number range");
        Applies(constraints.MinimumDuration is not null || constraints.MaximumDuration is not null, ConfigurationFieldKind.Duration, "duration range");
        Applies(constraints.MinimumLength is not null || constraints.MaximumLength is not null, ConfigurationFieldKind.Text, "length");
        Applies(constraints.AllowedCharacters is not null, ConfigurationFieldKind.Text, "allowed characters");
        Applies(constraints.Members.Count > 0, ConfigurationFieldKind.Enumeration, "members");
        Applies(constraints.MinimumItems is not null || constraints.MaximumItems is not null, ConfigurationFieldKind.Array, "item count");
        Applies(constraints.AllowedSchemes.Count > 0, ConfigurationFieldKind.Uri, "allowed schemes");

        if (constraints.MinimumInteger > constraints.MaximumInteger || constraints.MinimumNumber > constraints.MaximumNumber
            || constraints.MinimumDuration > constraints.MaximumDuration || constraints.MinimumLength > constraints.MaximumLength
            || constraints.MinimumItems > constraints.MaximumItems)
        {
            throw Invalid(ns, path, "a minimum is above its maximum");
        }

        if (constraints.MinimumLength < 0 || constraints.MinimumItems < 0)
        {
            throw Invalid(ns, path, "a minimum length or item count is negative");
        }

        if (constraints.MaximumLength > ConfigurationLimits.MaxStringBytes)
        {
            throw Invalid(ns, path, "a string maximum length is above 65,536");
        }

        if (kind == ConfigurationFieldKind.Enumeration)
        {
            if (constraints.Members.Count == 0)
            {
                throw Invalid(ns, path, "an enumeration has no members");
            }

            if (constraints.Members.Any(member => member is null) || constraints.Members.Distinct(StringComparer.Ordinal).Count() != constraints.Members.Count)
            {
                throw Invalid(ns, path, "enumeration members are not unique non-null strings");
            }

            // A member no document can hold could never be chosen, and it would make its default unreachable too.
            if (constraints.Members.Any(member => !IsDocumentText(member)))
            {
                throw Invalid(ns, path, "an enumeration member holds a control character or a lone surrogate, or is too long");
            }
        }

        if (constraints.AllowedSchemes.Any(scheme => scheme is null || !Uri.CheckSchemeName(scheme)))
        {
            throw Invalid(ns, path, "an allowed scheme is not a valid URI scheme");
        }

        if (constraints.AllowedCharacters is { } pattern && !PatternIsWellFormed(pattern))
        {
            throw Invalid(ns, path, "the allowed-character pattern allows nothing, or lists a character that is not ASCII punctuation, or lists one twice");
        }
    }

    private static bool PatternIsWellFormed(CharacterClassPattern pattern)
    {
        const CharacterClasses all = CharacterClasses.LowerCaseLetters | CharacterClasses.UpperCaseLetters | CharacterClasses.Digits;
        return (pattern.Classes & ~all) == 0
            && (pattern.Classes != CharacterClasses.None || pattern.Punctuation.Length > 0)
            && pattern.Punctuation.All(c => c is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(c))
            && pattern.Punctuation.Distinct().Count() == pattern.Punctuation.Length
            && (pattern.FirstCharacter is not { } first || (first != CharacterClasses.None && (first & ~all) == 0));
    }

    private static bool DefaultSatisfies(ConfigurationFieldKind kind, object value, FieldConstraints constraints) => kind switch
    {
        ConfigurationFieldKind.Text => value is string text && IsDocumentText(text) && FieldRules.LengthAllowed(text, constraints) && FieldRules.PatternAllowed(text, constraints),
        ConfigurationFieldKind.Enumeration => value is string member && FieldRules.MemberAllowed(member, constraints),
        ConfigurationFieldKind.WholeNumber => value is long integer && FieldRules.InRange(integer, constraints),
        ConfigurationFieldKind.Number => value is decimal number && FieldRules.InRange(number, constraints),
        ConfigurationFieldKind.Boolean => value is bool,
        ConfigurationFieldKind.Duration => value is TimeSpan duration && duration >= TimeSpan.Zero
            && duration.Ticks % TimeSpan.TicksPerMillisecond == 0 && FieldRules.InRange(duration, constraints),
        ConfigurationFieldKind.Uri => value is Uri uri && uri.IsAbsoluteUri && ScalarSyntax.IsUriSyntax(uri.OriginalString)
            && FieldRules.SchemeAllowed(uri, constraints),
        _ => false,
    };

    /// <summary>
    /// True when a document could hold the text: no control character other than tab, line feed, or carriage return, no
    /// lone surrogate, and within the string limit. Two lone surrogates both write as U+FFFD in the canonical form, so a
    /// default holding one would give two different values the same revision (contract, Canonical form rule 4).
    /// </summary>
    private static bool IsDocumentText(string text) =>
        !FieldRules.HasForbiddenControl(text) && !FieldRules.HasLoneSurrogate(text) && !FieldRules.TooLong(text);

    private static GatewayException Invalid(string ns, string? field, string rule) =>
        Faults.Create(ErrorCodes.ConfigSchemaInvalid, ("namespace", ns), ("field", field ?? "(none)"), ("rule", rule));
}
