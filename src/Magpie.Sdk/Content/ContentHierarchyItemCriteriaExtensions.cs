using System;
using System.Linq;

using Magpie.Models.Data;
using Magpie.Models.Filtering;

namespace Magpie.Sdk.Content;

/// <summary>
/// Extension methods for the metadata within content hierarchy item criteria.
/// </summary>
public static class ContentHierarchyItemCriteriaExtensions
{
    /// <summary>
    /// Merges the metadata criteria into one criteria set.
    /// </summary>
    /// <param name="criteria">The criteria that hold the metadata to merge</param>
    public static MetadataCriteria MergeMetadata(this ContentHierarchyItemCriteria criteria)
    {
        if (criteria is null)
        {
            throw new ArgumentNullException(nameof(criteria));
        }

        var sources = criteria.Metadata.And.ToArray();
        if (sources.Length == 1)
        {
            return sources[0];
        }

        var merged = new MetadataCriteria();
        foreach (var source in sources)
        {
            merged = merged with
            {
                ProviderType = MergeSets(merged.ProviderType, source.ProviderType),
                Source = MergeSets(merged.Source, source.Source),
                Title = MergeSets(merged.Title, source.Title),
                OriginalTitle = MergeSets(merged.OriginalTitle, source.OriginalTitle),
                Language = MergeSets(merged.Language, source.Language),
                Overview = MergeSets(merged.Overview, source.Overview),
                PublishDate = MergeSets(merged.PublishDate, source.PublishDate),
                Duration = MergeSets(merged.Duration, source.Duration),
                ProductionStatus = MergeSets(merged.ProductionStatus, source.ProductionStatus),
                Established = MergeSets(merged.Established, source.Established),
                Dissolved = MergeSets(merged.Dissolved, source.Dissolved),
                BirthDate = MergeSets(merged.BirthDate, source.BirthDate),
                DeathDate = MergeSets(merged.DeathDate, source.DeathDate),
                CustomMetadata = MergeSets(merged.CustomMetadata, source.CustomMetadata),
                Awards = MergeCollections(merged.Awards, source.Awards),
                ExternalIds = MergeCollections(merged.ExternalIds, source.ExternalIds),
                References = MergeCollections(merged.References, source.References),
                Relationships = MergeCollections(merged.Relationships, source.Relationships),
                Images = MergeCollections(merged.Images, source.Images),
                Previews = MergeCollections(merged.Previews, source.Previews),
                AlternateTexts = MergeCollections(merged.AlternateTexts, source.AlternateTexts),
                ContentRatings = MergeCollections(merged.ContentRatings, source.ContentRatings),
                Genres = MergeCollections(merged.Genres, source.Genres),
                Tags = MergeCollections(merged.Tags, source.Tags),
                PreferredLanguage = merged.PreferredLanguage ?? source.PreferredLanguage
            };
        }

        return merged;
    }

    private static CriteriaSet<T> MergeSets<T>(CriteriaSet<T> left, CriteriaSet<T> right) =>
        new() { And = [.. left.And, .. right.And] };

    private static CollectionCriteria<T> MergeCollections<T>(
        CollectionCriteria<T> left,
        CollectionCriteria<T> right) => new()
        {
            Some = [.. left.Some, .. right.Some],
            None = [.. left.None, .. right.None]
        };
}
