using System.Collections.Immutable;
using System.Globalization;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>
/// Closed validation against the registry (FR-018, FR-021, research R3). <see cref="CheckUnknown"/> runs on each layer
/// before merging, so an unknown name is reported against the layer that introduced it. <see cref="Validate"/> runs on
/// the merged tree, walks only declared fields, types every scalar by its field, and collects every error.
/// </summary>
internal sealed class SchemaValidator(ConfigurationSchemaRegistry registry, string? environmentName, FieldIssues issues)
{
    public const string Root = "iskender";
    private const string Placeholder = "secretRef";

    /// <summary>The <c>iskender</c> object of a document root, or null when absent. A non-object root is reported.</summary>
    public ObjectNode? Iskender(ObjectNode? document)
    {
        if (document?.Find(Root) is not { } member || IsNull(member.Value))
        {
            return null;
        }

        if (member.Value is ObjectNode tree)
        {
            return tree;
        }

        issues.Add(ErrorCodes.ConfigFieldTypeMismatch, Root, member.Value.Location,
            new ErrorText("must be an object of sections", "make iskender an object whose keys are registered sections."));
        return null;
    }

    /// <summary>Reports undeclared namespaces and fields in one layer's <c>iskender</c> tree.</summary>
    public void CheckUnknown(ObjectNode iskender)
    {
        foreach (var member in iskender.Members)
        {
            var path = $"{Root}.{member.Key}";
            if (registry.Find(member.Key) is not { } schema)
            {
                issues.Add(ErrorCodes.ConfigNamespaceUnknown, path, member.KeyLocation,
                    ErrorTextTemplates.NamespaceUnknown(registry.Schemas.Select(s => s.Namespace)));
            }
            else if (member.Value is ObjectNode section)
            {
                CheckUnknownFields(schema.Namespace, path, schema.Fields, section, isNamespace: true);
            }
        }
    }

    /// <summary>
    /// Reports every undeclared member. In an <c>object</c> field, a <c>secretRef</c> member makes the value a placeholder,
    /// which <see cref="Validate"/> reports as a type mismatch, so only that member is skipped. A namespace section is not
    /// a field, so a <c>secretRef</c> member there is an undeclared field like any other (contract, Root and namespaces rule 4).
    /// </summary>
    private void CheckUnknownFields(string ns, string path, IReadOnlyList<FieldSchema> fields, ObjectNode node, bool isNamespace = false)
    {
        var placeholder = !isNamespace && IsPlaceholderShaped(node);
        foreach (var member in node.Members)
        {
            if (placeholder && string.Equals(member.Key, Placeholder, StringComparison.Ordinal))
            {
                continue;
            }

            var childPath = $"{path}.{member.Key}";
            var field = fields.FirstOrDefault(f => string.Equals(f.Name, member.Key, StringComparison.Ordinal));
            if (field is null)
            {
                issues.Add(ErrorCodes.ConfigFieldUnknown, childPath, member.KeyLocation,
                    ErrorTextTemplates.FieldUnknown(ns, member.Key, fields.Select(f => f.Name)));
                continue;
            }

            CheckUnknownValue(ns, childPath, field, member.Value);
        }
    }

    private void CheckUnknownValue(string ns, string path, FieldSchema field, CanonicalNode value)
    {
        if (field.Kind == ConfigurationFieldKind.Section && value is ObjectNode section)
        {
            CheckUnknownFields(ns, path, field.Fields, section);
        }
        else if (field.Kind == ConfigurationFieldKind.Array && value is ArrayNode array && field.Items is { } items)
        {
            for (var index = 0; index < array.Items.Count; index++)
            {
                CheckUnknownValue(ns, $"{path}[{index}]", items, array.Items[index]);
            }
        }
    }

    /// <summary>Validates the merged tree. Returns each registered namespace's values, in ordinal order.</summary>
    public IReadOnlyList<(SectionSchema Schema, SectionValues Values)> Validate(ObjectNode? iskender)
    {
        var sections = new List<(SectionSchema, SectionValues)>();
        foreach (var schema in registry.Schemas)
        {
            var member = iskender?.Find(schema.Namespace);
            var path = $"{Root}.{schema.Namespace}";
            ObjectNode? node = null;
            if (member is not null && !IsNull(member.Value))
            {
                if (member.Value is ObjectNode section)
                {
                    node = section;
                }
                else
                {
                    issues.Add(ErrorCodes.ConfigFieldTypeMismatch, path, member.Value.Location,
                        new ErrorText("must be an object of the section's fields", $"make {path} an object."));
                }
            }

            // A root that only the environment created is located at (none), so it anchors nothing in a real layer.
            var anchor = node?.Location ?? iskender?.Location;
            sections.Add((schema, Fields(path, schema.Fields, node, anchor)));
        }

        return sections;
    }

    private SectionValues Fields(string path, IReadOnlyList<FieldSchema> fields, ObjectNode? node, ConfigurationSourceLocation? anchor)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var childPath = $"{path}.{field.Name}";
            var member = node?.Find(field.Name);
            var value = member is null || IsNull(member.Value) ? null : member.Value;

            if (field.Kind == ConfigurationFieldKind.Section)
            {
                ObjectNode? child = null;
                if (value is ObjectNode objectValue && !IsPlaceholderShaped(objectValue))
                {
                    child = objectValue;
                }
                else if (value is not null)
                {
                    TypeMismatch(childPath, field, value);
                }

                var childValues = Fields(childPath, field.Fields, child, child?.Location ?? anchor);

                // An object's default is its fields' defaults, so a development-only object is unsafe only when a value set
                // below it differs from its default (FR-016).
                if (child is not null && field.IsDevelopmentOnly && !IsDevelopment && !HoldsDefaults(childValues, child))
                {
                    issues.Add(ErrorCodes.ConfigValueUnsafeForEnvironment, childPath, child.Location, ErrorTextTemplates.UnsafeForEnvironment());
                }

                values[field.Name] = childValues;
                continue;
            }

            if (value is null)
            {
                if (field.Default is { } fallback)
                {
                    values[field.Name] = fallback;
                }
                else if (field.IsRequired)
                {
                    issues.Add(ErrorCodes.ConfigFieldRequired, childPath, anchor ?? ConfigurationSourceLocation.None, ErrorTextTemplates.Required(childPath));
                }

                continue;
            }

            if (Typed(childPath, field, value) is { } typed)
            {
                if (field.IsDevelopmentOnly && !IsDevelopment && !IsDefault(typed, field.Default))
                {
                    issues.Add(ErrorCodes.ConfigValueUnsafeForEnvironment, childPath, value.Location, ErrorTextTemplates.UnsafeForEnvironment());
                    continue;
                }

                values[field.Name] = typed;
            }
        }

        return new SectionValues(fields, values);
    }

    /// <summary>Types one present, non-null value by its field, or reports why it cannot and returns null.</summary>
    private object? Typed(string path, FieldSchema field, CanonicalNode value)
    {
        if (field.Kind == ConfigurationFieldKind.SecretReference)
        {
            return SecretValue(path, value);
        }

        if (field.Kind == ConfigurationFieldKind.Array)
        {
            return ArrayValue(path, field, value);
        }

        if (field.Kind == ConfigurationFieldKind.Section)
        {
            if (value is ObjectNode item && !IsPlaceholderShaped(item))
            {
                return Fields(path, field.Fields, item, item.Location);
            }

            TypeMismatch(path, field, value);
            return null;
        }

        if (value is not ScalarNode scalar)
        {
            TypeMismatch(path, field, value);
            return null;
        }

        return field.Kind switch
        {
            ConfigurationFieldKind.Text => TextValue(path, field, scalar),
            ConfigurationFieldKind.Enumeration => EnumerationValue(path, field, scalar),
            ConfigurationFieldKind.WholeNumber => IntegerValue(path, field, scalar),
            ConfigurationFieldKind.Number => NumberValue(path, field, scalar),
            ConfigurationFieldKind.Boolean => BooleanValue(path, field, scalar),
            ConfigurationFieldKind.Duration => DurationValue(path, field, scalar),
            _ => UriValue(path, field, scalar),
        };
    }

    private ImmutableArray<object>? ArrayValue(string path, FieldSchema field, CanonicalNode value)
    {
        if (value is not ArrayNode array)
        {
            TypeMismatch(path, field, value);
            return null;
        }

        if (!FieldRules.ItemCountAllowed(array.Items.Count, field.Constraints))
        {
            issues.Add(ErrorCodes.ConfigFieldOutOfRange, path, array.Location, ErrorTextTemplates.OutOfRange(field));
        }

        var items = ImmutableArray.CreateBuilder<object>(array.Items.Count);
        var valid = true;
        for (var index = 0; index < array.Items.Count; index++)
        {
            var itemPath = $"{path}[{index}]";
            var item = array.Items[index];
            if (IsNull(item))
            {
                TypeMismatch(itemPath, field.Items!, item);
                valid = false;
                continue;
            }

            if (Typed(itemPath, field.Items!, item) is { } typed)
            {
                items.Add(typed);
            }
            else
            {
                valid = false;
            }
        }

        return valid ? items.MoveToImmutable() : null;
    }

    private SecretReference? SecretValue(string path, CanonicalNode value)
    {
        if (value is not ObjectNode node || node.Find(Placeholder) is null)
        {
            issues.Add(ErrorCodes.ConfigSecretPlaintext, path, value.Location, ErrorTextTemplates.SecretPlaintext());
            return null;
        }

        if (node.Members.Count != 1 || node.Members[0].Value is not ScalarNode { Kind: ScalarKind.String or ScalarKind.Plain } name
            || !SecretReference.IsValidName(name.Text))
        {
            issues.Add(ErrorCodes.ConfigSecretReferenceMalformed, path, value.Location, ErrorTextTemplates.SecretReferenceMalformed());
            return null;
        }

        return new SecretReference(name.Text);
    }

    private string? TextValue(string path, FieldSchema field, ScalarNode scalar)
    {
        if (scalar.Kind is not (ScalarKind.String or ScalarKind.Plain))
        {
            return TypeMismatch<string>(path, field, scalar);
        }

        if (!FieldRules.LengthAllowed(scalar.Text, field.Constraints))
        {
            issues.Add(ErrorCodes.ConfigFieldOutOfRange, path, scalar.Location, ErrorTextTemplates.OutOfRange(field));
            return null;
        }

        if (!FieldRules.PatternAllowed(scalar.Text, field.Constraints))
        {
            issues.Add(ErrorCodes.ConfigFieldInvalidFormat, path, scalar.Location, ErrorTextTemplates.InvalidFormat(field));
            return null;
        }

        return scalar.Text;
    }

    private string? EnumerationValue(string path, FieldSchema field, ScalarNode scalar)
    {
        if (scalar.Kind is not (ScalarKind.String or ScalarKind.Plain))
        {
            return TypeMismatch<string>(path, field, scalar);
        }

        if (!FieldRules.MemberAllowed(scalar.Text, field.Constraints))
        {
            issues.Add(ErrorCodes.ConfigFieldInvalidFormat, path, scalar.Location, ErrorTextTemplates.InvalidFormat(field));
            return null;
        }

        return scalar.Text;
    }

    private object? IntegerValue(string path, FieldSchema field, ScalarNode scalar)
    {
        if (scalar.Kind is not (ScalarKind.Number or ScalarKind.Plain) || !ScalarSyntax.IsInteger(scalar.Text))
        {
            return TypeMismatch<object>(path, field, scalar);
        }

        if (!long.TryParse(scalar.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            || !FieldRules.InRange(integer, field.Constraints))
        {
            issues.Add(ErrorCodes.ConfigFieldOutOfRange, path, scalar.Location, ErrorTextTemplates.OutOfRange(field));
            return null;
        }

        return integer;
    }

    private object? NumberValue(string path, FieldSchema field, ScalarNode scalar)
    {
        if (scalar.Kind is not (ScalarKind.Number or ScalarKind.Plain) || !ScalarSyntax.IsNumber(scalar.Text))
        {
            return TypeMismatch<object>(path, field, scalar);
        }

        if (ScalarSyntax.ExactDecimal(scalar.Text) is not { } number || !FieldRules.InRange(number, field.Constraints))
        {
            issues.Add(ErrorCodes.ConfigFieldOutOfRange, path, scalar.Location, ErrorTextTemplates.OutOfRange(field));
            return null;
        }

        return number;
    }

    private object? BooleanValue(string path, FieldSchema field, ScalarNode scalar) =>
        scalar.Kind is ScalarKind.Boolean or ScalarKind.Plain && scalar.Text is "true" or "false"
            ? scalar.Text == "true"
            : TypeMismatch<object>(path, field, scalar);

    private object? DurationValue(string path, FieldSchema field, ScalarNode scalar)
    {
        if (scalar.Kind is not (ScalarKind.String or ScalarKind.Plain))
        {
            return TypeMismatch<object>(path, field, scalar);
        }

        switch (ScalarSyntax.Duration(scalar.Text, out var duration))
        {
            case ScalarSyntax.DurationResult.Invalid:
                issues.Add(ErrorCodes.ConfigFieldInvalidFormat, path, scalar.Location, ErrorTextTemplates.InvalidFormat(field));
                return null;
            case ScalarSyntax.DurationResult.TooLarge:
                issues.Add(ErrorCodes.ConfigFieldOutOfRange, path, scalar.Location, ErrorTextTemplates.OutOfRange(field));
                return null;
        }

        if (!FieldRules.InRange(duration, field.Constraints))
        {
            issues.Add(ErrorCodes.ConfigFieldOutOfRange, path, scalar.Location, ErrorTextTemplates.OutOfRange(field));
            return null;
        }

        return duration;
    }

    private Uri? UriValue(string path, FieldSchema field, ScalarNode scalar)
    {
        if (scalar.Kind is not (ScalarKind.String or ScalarKind.Plain))
        {
            return TypeMismatch<Uri>(path, field, scalar);
        }

        if (!ScalarSyntax.StartsWithScheme(scalar.Text) || !Uri.TryCreate(scalar.Text, UriKind.Absolute, out var uri))
        {
            issues.Add(ErrorCodes.ConfigFieldInvalidFormat, path, scalar.Location, ErrorTextTemplates.InvalidFormat(field));
            return null;
        }

        if (!FieldRules.SchemeAllowed(uri, field.Constraints))
        {
            issues.Add(ErrorCodes.ConfigFieldInvalidFormat, path, scalar.Location, ErrorTextTemplates.InvalidFormat(field, scheme: true));
            return null;
        }

        return uri;
    }

    private T? TypeMismatch<T>(string path, FieldSchema field, CanonicalNode value)
        where T : class
    {
        TypeMismatch(path, field, value);
        return null;
    }

    private void TypeMismatch(string path, FieldSchema field, CanonicalNode value) =>
        issues.Add(ErrorCodes.ConfigFieldTypeMismatch, path, value.Location,
            value is ObjectNode node && IsPlaceholderShaped(node) ? ErrorTextTemplates.PlaceholderInPlainField(field) : ErrorTextTemplates.TypeMismatch(field));

    /// <summary>Only <c>development</c> permits development-only values. Any other name, and no name, is production-grade.</summary>
    private bool IsDevelopment => string.Equals(environmentName, "development", StringComparison.Ordinal);

    /// <summary>
    /// A URI matches its default by its exact validated text, the form the canonical writer hashes, since <see cref="Uri.Equals(object)"/>
    /// ignores user info and the fragment and folds the scheme and host case (FR-016).
    /// </summary>
    private static bool IsDefault(object typed, object? fallback) =>
        typed is Uri uri ? fallback is Uri defaultUri && string.Equals(uri.OriginalString, defaultUri.OriginalString, StringComparison.Ordinal) : Equals(typed, fallback);

    /// <summary>
    /// True when every member set below the object holds its field's default. A value that failed validation, a placeholder,
    /// and a value for a field with no default all differ. Null members are already dropped, and an empty object is absent.
    /// </summary>
    private static bool HoldsDefaults(SectionValues values, ObjectNode node) =>
        values.Fields.All(field => node.Find(field.Name) is not { } member || IsNull(member.Value)
            || (field.Kind == ConfigurationFieldKind.Section
                ? member.Value is ObjectNode child && !IsPlaceholderShaped(child)
                    && values.Values.TryGetValue(field.Name, out var nested) && HoldsDefaults((SectionValues)nested, child)
                : values.Values.TryGetValue(field.Name, out var typed) && IsDefault(typed, field.Default)));

    /// <summary>An object with a member named exactly <c>secretRef</c>, which no schema can declare.</summary>
    internal static bool IsPlaceholderShaped(ObjectNode node) => node.Find(Placeholder) is not null;

    internal static bool IsNull(CanonicalNode node) => node is ScalarNode { Kind: ScalarKind.Null };
}
