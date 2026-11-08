using Iskender.Contracts.Compatibility.Tests.Surface;

namespace Iskender.Contracts.Compatibility.Tests.Registries;

/// <summary>
/// Classifies registry differences with the contract's Registries table (G1 to G5). An added code or resource type, a
/// newly retired one, and a safe-message change are compatible; rule H4 still checks the published tables.
/// </summary>
public static class RegistryClassifier
{
    public static IReadOnlyList<SurfaceChange> Compare(RegistrySnapshot before, RegistrySnapshot after)
    {
        var breaks = new List<SurfaceChange>();

        var categories = after.ErrorCategories.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        foreach (var category in before.ErrorCategories)
        {
            if (!categories.TryGetValue(category.Id, out var current))
            {
                breaks.Add(new("G5", $"error category {category.Id}", "category removed"));
            }
            else if (category.RetryRule != current.RetryRule)
            {
                breaks.Add(new("G5", $"error category {category.Id}", $"retry rule changed from {category.RetryRule} to {current.RetryRule}"));
            }
        }

        foreach (var added in after.ErrorCategories.Where(entry => before.ErrorCategories.All(old => old.Id != entry.Id)))
        {
            breaks.Add(new("G5", $"error category {added.Id}", "category added"));
        }

        var codes = after.ErrorCodes.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        foreach (var code in before.ErrorCodes)
        {
            var item = $"error code {code.Id}";
            if (!codes.TryGetValue(code.Id, out var current))
            {
                breaks.Add(new("G1", item, "code removed"));
                continue;
            }

            if (code.Category != current.Category)
            {
                breaks.Add(new("G2", item, $"category changed from {code.Category} to {current.Category}"));
            }

            if (code.Meaning != current.Meaning)
            {
                breaks.Add(new("G3", item, "meaning changed"));
            }

            if (code.Status == "retired" && current.Status == "active")
            {
                breaks.Add(new("G3", item, "retired code became active again"));
            }
        }

        var types = after.ResourceTypes.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
        foreach (var type in before.ResourceTypes)
        {
            var item = $"resource type {type.Name}";
            if (!types.TryGetValue(type.Name, out var current))
            {
                breaks.Add(new("G4", item, "resource type removed"));
                continue;
            }

            if (type.Prefix != current.Prefix)
            {
                breaks.Add(new("G4", item, $"prefix changed from {type.Prefix} to {current.Prefix}"));
            }

            if (type.Status == "retired" && current.Status == "active")
            {
                breaks.Add(new("G4", item, "retired type became active again"));
            }
        }

        return breaks;
    }
}
