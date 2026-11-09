namespace Iskender.Domain.Registry;

/// <summary>
/// Merges the <c>iskender</c> trees of each layer, lowest first: base, overlay, then environment (research R5). Objects
/// merge by key and keep the lowest layer's location; a scalar, array, or placeholder at a higher layer replaces the lower
/// node whole and keeps its own location. A <c>null</c> at any layer is dropped first, so it never removes a lower value, except as
/// a placeholder's name. Schema defaults are applied afterwards by the validator, as the lowest layer.
/// </summary>
internal static class OverlayMerger
{
    public static ObjectNode? Merge(params ObjectNode?[] layers)
    {
        ObjectNode? merged = null;
        foreach (var layer in layers)
        {
            if (layer is not null)
            {
                merged = merged is null ? (ObjectNode)WithoutNulls(layer) : MergeObjects(merged, layer);
            }
        }

        return merged;
    }

    /// <summary>
    /// A merged object keeps the location of the lowest layer that holds it, so a missing required field is located at its
    /// nearest present ancestor in its real layer (contract, Field errors rule 2).
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
            if (IsAbsent(member))
            {
                continue;
            }

            merged.Set(merged.Find(member.Key) is { Value: ObjectNode lowerObject } lowerMember && member.Value is ObjectNode higherObject
                && Merges(lowerObject) && Merges(higherObject)
                ? lowerMember with { Value = MergeObjects(lowerObject, higherObject) }
                : new ObjectMember(member.Key, member.KeyLocation, WithoutNulls(member.Value)));
        }

        return merged;
    }

    /// <summary>
    /// A placeholder is replaced whole, like a scalar, so it keeps the location and layer of the layer that set it
    /// (contract, Field errors rule 2, Environment variables rule 7).
    /// </summary>
    private static bool Merges(ObjectNode node) => !SchemaValidator.IsPlaceholderShaped(node);

    /// <summary>
    /// A <c>null</c> member is absent, except a placeholder's <c>secretRef</c>: that null is the placeholder's malformed
    /// name, kept so the validator reports <c>config.secret.reference_malformed</c> (contract, Secret reference placeholders rule 5).
    /// </summary>
    private static bool IsAbsent(ObjectMember member) =>
        SchemaValidator.IsNull(member.Value) && !string.Equals(member.Key, "secretRef", StringComparison.Ordinal);

    /// <summary>A copy of the node with every absent object member removed. Array items are kept, so the validator reports a <c>null</c> item.</summary>
    private static CanonicalNode WithoutNulls(CanonicalNode node)
    {
        switch (node)
        {
            case ObjectNode source:
                var copy = new ObjectNode(source.Location);
                foreach (var member in source.Members.Where(member => !IsAbsent(member)))
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
}
