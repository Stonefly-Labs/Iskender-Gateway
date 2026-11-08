namespace Iskender.Domain.Registry;

/// <summary>
/// Merges the <c>iskender</c> trees of each layer, lowest first: base, overlay, then environment (research R5). Objects
/// merge by key; a scalar or array at a higher layer replaces the lower node whole and keeps its own location. A
/// <c>null</c> at any layer is dropped first, so it never removes a lower value. Schema defaults are applied afterwards
/// by the validator, as the lowest layer.
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

    private static ObjectNode MergeObjects(ObjectNode lower, ObjectNode higher)
    {
        var merged = new ObjectNode(higher.Location);
        foreach (var member in lower.Members)
        {
            merged.TryAdd(member);
        }

        foreach (var member in higher.Members)
        {
            if (SchemaValidator.IsNull(member.Value))
            {
                continue;
            }

            var value = merged.Find(member.Key)?.Value is ObjectNode lowerObject && member.Value is ObjectNode higherObject
                ? MergeObjects(lowerObject, higherObject)
                : WithoutNulls(member.Value);
            merged.Set(new ObjectMember(member.Key, member.KeyLocation, value));
        }

        return merged;
    }

    /// <summary>A copy of the node with every <c>null</c> object member removed. Array items are kept, so the validator reports a <c>null</c> item.</summary>
    private static CanonicalNode WithoutNulls(CanonicalNode node)
    {
        switch (node)
        {
            case ObjectNode source:
                var copy = new ObjectNode(source.Location);
                foreach (var member in source.Members.Where(member => !SchemaValidator.IsNull(member.Value)))
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
