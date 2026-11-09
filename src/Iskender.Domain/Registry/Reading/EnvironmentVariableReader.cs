using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>
/// Maps <c>ISKENDER__</c> variables onto declared paths under <c>iskender</c> (research R6, contract Environment
/// variables). Names match without regard to ASCII case, so the result is the same under every culture. The tree it
/// builds holds declared names only, so the environment layer needs no separate unknown-name check.
/// </summary>
internal static class EnvironmentVariableReader
{
    private const string Separator = "__";
    private const string SecretSegment = "SECRETREF";

    /// <summary>The variables' <c>iskender</c> tree, or null when none applied, and how many applied.</summary>
    public static (ObjectNode? Iskender, int Applied) Read(ConfigurationEnvironmentSource? source, ConfigurationSchemaRegistry registry, FieldIssues issues)
    {
        if (source is null)
        {
            return (null, 0);
        }

        // A variable whose text is malformed still maps to its path, so a second variable on that path is a conflict too.
        var mapped = new List<(string Variable, string Path, IReadOnlyList<string> Keys, CanonicalNode? Value)>();
        foreach (var (variable, text) in source.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (ConfigurationEnvironmentSource.HasPrefix(variable) && Map(variable, text, registry, issues) is { } entry)
            {
                mapped.Add((variable, entry.Path, entry.Keys, entry.Value));
            }
        }

        ObjectNode? root = null;
        var applied = 0;
        foreach (var group in mapped.GroupBy(entry => entry.Path, StringComparer.Ordinal))
        {
            var entries = group.ToList();
            if (entries.Count > 1)
            {
                // Variables are taken in ordinal order, so the first entry is the name that sorts first.
                issues.Add(ErrorCodes.ConfigOverlayConflict, group.Key, Location(entries[0].Variable),
                    ErrorTextTemplates.OverlayConflict(entries[0].Variable, entries[1].Variable));
                continue;
            }

            var (variable, _, keys, value) = entries[0];
            if (value is null || SchemaValidator.IsNull(value))
            {
                continue;
            }

            // Nothing a variable created is a present ancestor, so a missing required field below it is located at the nearest
            // node a document holds, or at (none) in layer defaults (contract, Field errors rule 2). The validator skips a
            // namespace or object in layer environment, and the root has no location to skip.
            root ??= new ObjectNode(ConfigurationSourceLocation.None);
            var node = root;
            for (var index = 0; index < keys.Count - 1; index++)
            {
                if (node.Find(keys[index])?.Value is not ObjectNode child)
                {
                    child = new ObjectNode(Location(variable));
                    node.TryAdd(new ObjectMember(keys[index], Location(variable), child));
                }

                node = child;
            }

            node.TryAdd(new ObjectMember(keys[^1], Location(variable), value));

            // A placeholder whose name is null is absent over a lower value and malformed without one, so it is never applied
            // to a snapshot (contract, Snapshot rule 2, Precedence rule 3).
            if (!OverlayMerger.IsNullPlaceholder(value))
            {
                applied++;
            }
        }

        return (root, applied);
    }

    private static ConfigurationSourceLocation Location(string variable) => new(variable, 0, 0, ConfigurationLayer.Environment);

    /// <summary>The variable's path and value, or a null value when its text is malformed, or null when it names no declared scalar.</summary>
    private static (string Path, IReadOnlyList<string> Keys, CanonicalNode? Value)? Map(string variable, string text, ConfigurationSchemaRegistry registry, FieldIssues issues)
    {
        var location = Location(variable);
        var segments = variable[ConfigurationEnvironmentSource.Prefix.Length..].Split(Separator);
        var secret = segments.Length > 1 && FieldRules.IgnoreAsciiCase.Equals(segments[^1], SecretSegment);
        if (secret)
        {
            segments = segments[..^1];
        }

        if (segments.Any(segment => segment.Length == 0))
        {
            var path = string.Join('.', segments.Prepend(SchemaValidator.Root));
            issues.Add(ErrorCodes.ConfigFieldUnknown, path, location, ErrorTextTemplates.VariableNamesNoPath());
            return null;
        }

        var schema = registry.Schemas.FirstOrDefault(s => FieldRules.IgnoreAsciiCase.Equals(s.Namespace, segments[0].Replace('_', '-')));
        if (schema is null)
        {
            issues.Add(ErrorCodes.ConfigNamespaceUnknown, $"{SchemaValidator.Root}.{segments[0]}", location,
                ErrorTextTemplates.NamespaceUnknown(registry.Schemas.Select(s => s.Namespace)));
            return null;
        }

        var keys = new List<string> { schema.Namespace };
        var current = $"{SchemaValidator.Root}.{schema.Namespace}";
        IReadOnlyList<FieldSchema> fields = schema.Fields;
        FieldSchema? field = null;
        foreach (var segment in segments.Skip(1))
        {
            if (field is not null)
            {
                if (field.Kind == ConfigurationFieldKind.Array)
                {
                    break;
                }

                fields = field.Kind == ConfigurationFieldKind.Section ? field.Fields : [];
            }

            field = fields.FirstOrDefault(f => FieldRules.IgnoreAsciiCase.Equals(f.Name, segment));
            if (field is null)
            {
                issues.Add(ErrorCodes.ConfigFieldUnknown, $"{current}.{segment}", location,
                    ErrorTextTemplates.FieldUnknown(schema.Namespace, segment, fields.Select(f => f.Name)));
                return null;
            }

            keys.Add(field.Name);
            current = $"{current}.{field.Name}";
        }

        if (field is null)
        {
            issues.Add(ErrorCodes.ConfigFieldTypeMismatch, current, location, ErrorTextTemplates.VariableNamesSection());
            return null;
        }

        if (field.Kind is ConfigurationFieldKind.Section or ConfigurationFieldKind.Array)
        {
            // The variable names an object or array field, or a path through an array.
            issues.Add(ErrorCodes.ConfigFieldTypeMismatch, current, location, ErrorTextTemplates.VariableNotScalar(field));
            return null;
        }

        if (DocumentBytes.CheckDecoded(text) is { } code)
        {
            issues.Add(code, current, location, ErrorTextTemplates.VariableText(code));
            return (current, keys, null);
        }

        var scalar = new ScalarNode(location, text, text is "" or "~" or "null" ? ScalarKind.Null : ScalarKind.Plain);
        if (!secret)
        {
            return (current, keys, scalar);
        }

        // A __SECRETREF segment always sets a placeholder; the validator reports it on a field that is not a secret. Empty,
        // ~, or null text is a null name, not an absent field, so it is config.secret.reference_malformed.
        var placeholder = new ObjectNode(location);
        placeholder.TryAdd(new ObjectMember("secretRef", location, scalar));
        return (current, keys, placeholder);
    }
}
