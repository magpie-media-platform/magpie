using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal static class CardigannDefinitionExtensions
{
    public static IReadOnlyList<CategoryMapping> ResolveCategoryMappings(this CardigannDefinition definition)
    {
        var mappings = new List<CategoryMapping>();
        var seen = new HashSet<(string Id, string Cat)>();

        foreach (var mapping in definition.Caps.CategoryMappings)
        {
            Add(mappings, seen, mapping.Id, mapping.Cat);
        }

        foreach (var pair in definition.Categories)
        {
            Add(mappings, seen, pair.Value, pair.Key);
        }

        return mappings;
    }

    private static void Add(
        ICollection<CategoryMapping> mappings,
        ISet<(string Id, string Cat)> seen,
        string id,
        string cat)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cat))
        {
            return;
        }

        var trimmedId = id.Trim();
        var trimmedCat = cat.Trim();
        if (!seen.Add((trimmedId.ToLowerInvariant(), trimmedCat.ToLowerInvariant())))
        {
            return;
        }

        mappings.Add(new CategoryMapping { Id = trimmedId, Cat = trimmedCat });
    }
}
