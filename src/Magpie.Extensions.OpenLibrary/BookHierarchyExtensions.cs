using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.OpenLibrary;

internal static class BookHierarchyExtensions
{
    internal static IReadOnlyList<BookHierarchy> ToBookHierarchies(
        this IEnumerable<IContentTypeDefinitionProvider> contentTypeDefinitionProviders)
    {
        foreach (var provider in contentTypeDefinitionProviders)
        {
            var hierarchies = FromDefinition(provider.Definition);
            if (hierarchies.Count > 0)
            {
                return hierarchies;
            }
        }

        return [];
    }

    internal static BookHierarchy? FindBookHierarchy(
        this IReadOnlyList<BookHierarchy> hierarchies,
        string contentType,
        string? level)
    {
        var matches = hierarchies
            .Where(hierarchy =>
                string.Equals(hierarchy.ContentTypeSlug, contentType, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(level) ||
                    string.Equals(hierarchy.BookLevelSlug, level, StringComparison.Ordinal)))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    internal static BookHierarchy? FindBookHierarchy(
        this IReadOnlyList<BookHierarchy> hierarchies,
        string level)
    {
        var matches = hierarchies
            .Where(hierarchy => string.Equals(
                hierarchy.BookLevelSlug,
                level,
                StringComparison.Ordinal))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static IReadOnlyList<BookHierarchy> FromDefinition(ContentTypeDefinition? definition)
    {
        if (definition is null)
        {
            return [];
        }

        return
        [
            .. definition.LevelDefinitions
                .Where(IsBookLevel)
                .Select(level => new BookHierarchy
                {
                    ContentTypeSlug = definition.Slug,
                    BookLevelSlug = level.Slug
                })
        ];
    }

    private static bool IsBookLevel(ContentHierarchyLevelDefinition level)
    {
        return string.IsNullOrWhiteSpace(level.ParentSlug) &&
            HasFields(
                level,
                MetadataField.Language |
                MetadataField.PublishDate |
                MetadataField.ExternalIds |
                MetadataField.Images |
                MetadataField.ContentRatings |
                MetadataField.Relationships |
                MetadataField.SequenceNumber);
    }

    private static bool HasFields(ContentHierarchyLevelDefinition level, MetadataField fields)
    {
        return (level.SupportedMetadataFields & fields) == fields;
    }
}
