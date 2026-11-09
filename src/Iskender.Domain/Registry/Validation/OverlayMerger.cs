namespace Iskender.Domain.Registry;

/// <summary>
/// Merges the <c>iskender</c> trees of each layer, lowest first: base, overlay, then environment (research R5). Objects
/// merge by key and keep the lowest layer's location; a scalar, array, or placeholder at a higher layer replaces the lower
/// node whole and keeps its own location. A <c>null</c> at any layer is dropped first, so it never removes a lower value, except as
/// a standalone placeholder's name. Schema defaults are applied afterwards by the validator, as the lowest layer.
/// </summary>
internal static class OverlayMerger
{
    private const string Placeholder = "secretRef";

    public static ObjectNode? Merge(params ObjectNode?[] layers)
    {
        ObjectNode? merged = null;
        foreach (var layer in layers)
        {
            if (layer is not null)
            {
                var cleaned = (ObjectNode)WithoutNulls(layer);
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
    /// A copy of the node with every <c>null</c> object member removed. A <c>null</c> <c>secretRef</c> is kept only as the
    /// sole member, a standalone placeholder's malformed name, so the validator reports <c>config.secret.reference_malformed</c>
    /// (contract, Secret reference placeholders rule 5). Array items are kept, so the validator reports a <c>null</c> item.
    /// </summary>
    private static CanonicalNode WithoutNulls(CanonicalNode node)
    {
        switch (node)
        {
            case ObjectNode source:
                var kept = source.Members.Where(member => !SchemaValidator.IsNull(member.Value) || IsPlaceholderName(member)).ToList();
                if (kept.Count > 1)
                {
                    kept.RemoveAll(member => IsPlaceholderName(member) && SchemaValidator.IsNull(member.Value));
                }

                var copy = new ObjectNode(source.Location);
                foreach (var member in kept)
                {
                    copy.TryAdd(member with { Value = WithoutNulls(member.Value) });
                }

                return copy;
            case ArrayNode array:
                var items = new ArrayNode(array.Location);
                items.Items.AddRange(array.Items.Select(item => item is ObjectNode ? WithoutNulls(item) : item));
                return items;
            default:
                return node;
        }
    }

    private static bool IsPlaceholderName(ObjectMember member) => string.Equals(member.Key, Placeholder, StringComparison.Ordinal);
}
