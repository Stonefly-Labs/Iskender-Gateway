using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Registry;

/// <summary>
/// Merges the <c>iskender</c> trees of each layer, lowest first: base, overlay, then environment (research R5). Objects
/// merge by key and keep the lowest layer's location; a scalar, array, or placeholder at a higher layer replaces the lower
/// node whole and keeps its own location. A <c>null</c> at any layer is dropped first, so it never removes a lower value, except as
/// a standalone placeholder's name or a <c>secretRef</c> in a secret field. Schema defaults are applied afterwards by the validator, as the lowest layer.
/// </summary>
internal static class OverlayMerger
{
    private const string Placeholder = "secretRef";

    public static ObjectNode? Merge(ConfigurationSchemaRegistry registry, params ObjectNode?[] layers)
    {
        ObjectNode? merged = null;
        foreach (var layer in layers)
        {
            if (layer is not null)
            {
                var cleaned = WithoutNulls(layer, registry);
                merged = merged is null ? cleaned : MergeObjects(merged, cleaned);
            }
        }

        return merged;
    }

    /// <summary>
    /// A merged object keeps the location of the lowest layer that holds it, so a missing required field is located at its
    /// nearest present ancestor in its real layer (contract, Field errors rule 2). Both objects are already without nulls.
    /// </summary>
    private static ObjectNode MergeObjects(ObjectNode lower, ObjectNode higher)
    {
        var merged = new ObjectNode(lower.Location);
        foreach (var member in lower.Members)
        {
            merged.TryAdd(member);
        }

        foreach (var member in higher.Members)
        {
            var lowerMember = merged.Find(member.Key);

            // A placeholder whose name is null is absent over a lower value, like any other null (contract, Precedence rule 3).
            if (lowerMember is not null && IsNullPlaceholder(member.Value))
            {
                continue;
            }

            merged.Set(lowerMember is { Value: ObjectNode lowerObject } && member.Value is ObjectNode higherObject
                && Merges(lowerObject) && Merges(higherObject)
                ? lowerMember with { Value = MergeObjects(lowerObject, higherObject) }
                : member);
        }

        return merged;
    }

    /// <summary>
    /// A placeholder is replaced whole, like a scalar, so it keeps the location and layer of the layer that set it
    /// (contract, Field errors rule 2, Environment variables rule 7).
    /// </summary>
    private static bool Merges(ObjectNode node) => !SchemaValidator.IsPlaceholderShaped(node);

    private static bool IsNullPlaceholder(CanonicalNode node) =>
        node is ObjectNode { Members: [{ Key: Placeholder } only] } && SchemaValidator.IsNull(only.Value);

    /// <summary>
    /// A copy of the <c>iskender</c> tree with every <c>null</c> object member removed. A <c>null</c> <c>secretRef</c> is kept
    /// as the sole member, a standalone placeholder's malformed name, and beside other members in a field declared as a
    /// secret reference, so the validator reports <c>config.secret.reference_malformed</c> for both, as it does for a
    /// <c>secretRef</c> of 5 (contract, Secret reference placeholders rule 5). Elsewhere it is dropped like any other
    /// <c>null</c>. Array items are kept, so the validator reports a <c>null</c> item.
    /// </summary>
    private static ObjectNode WithoutNulls(ObjectNode root, ConfigurationSchemaRegistry registry)
    {
        var copy = new ObjectNode(root.Location);
        foreach (var member in root.Members.Where(member => !SchemaValidator.IsNull(member.Value)))
        {
            var fields = registry.Schemas.FirstOrDefault(schema => string.Equals(schema.Namespace, member.Key, StringComparison.Ordinal))?.Fields;
            copy.TryAdd(member with { Value = WithoutNulls(member.Value, fields, secret: false) });
        }

        return copy;
    }

    /// <summary>The same for a node whose object members are <paramref name="fields"/>, or undeclared when null.</summary>
    private static CanonicalNode WithoutNulls(CanonicalNode node, IReadOnlyList<FieldSchema>? fields, bool secret)
    {
        switch (node)
        {
            case ObjectNode source:
                var kept = source.Members.Where(member => !SchemaValidator.IsNull(member.Value) || IsPlaceholderName(member)).ToList();
                if (kept.Count > 1 && !secret)
                {
                    kept.RemoveAll(member => IsPlaceholderName(member) && SchemaValidator.IsNull(member.Value));
                }

                var copy = new ObjectNode(source.Location);
                foreach (var member in kept)
                {
                    var field = fields?.FirstOrDefault(f => string.Equals(f.Name, member.Key, StringComparison.Ordinal));
                    copy.TryAdd(member with { Value = WithoutNulls(member.Value, field) });
                }

                return copy;
            default:
                return node;
        }
    }

    private static CanonicalNode WithoutNulls(CanonicalNode node, FieldSchema? field)
    {
        if (node is not ArrayNode array)
        {
            return WithoutNulls(node, field?.Kind == ConfigurationFieldKind.Section ? field.Fields : null, field?.Kind == ConfigurationFieldKind.SecretReference);
        }

        var item = field?.Kind == ConfigurationFieldKind.Array ? field.Items : null;
        var items = new ArrayNode(array.Location);
        items.Items.AddRange(array.Items.Select(value => value is ObjectNode ? WithoutNulls(value, item) : value));
        return items;
    }

    private static bool IsPlaceholderName(ObjectMember member) => string.Equals(member.Key, Placeholder, StringComparison.Ordinal);
}
