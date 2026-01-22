using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Content;
using Magpie.Models.Metadata;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides metadata for content hierarchy items from an external source.
/// </summary>
public interface IContentHierarchyMetadataProvider
{
    /// <summary>
    /// Gets the content type slugs supported by the provider.
    /// </summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>
    /// Fetches metadata for a content hierarchy level.
    /// </summary>
    /// <param name="contentHierarchyLevel">The content hierarchy level to fetch metadata for.</param>
    /// <param name="existingMetadata">The existing metadata contributions for the content hierarchy level.</param>
    /// <param name="ancestorLanguagePreferences">The language preferences of the nearest ancestor.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for fetching metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<MetadataContribution>> GetMetadataAsync(
        ContentHierarchyLevel contentHierarchyLevel,
        IEnumerable<MetadataContribution> existingMetadata,
        IEnumerable<string>? ancestorLanguagePreferences,
        string defaultLanguagePreference,
        CancellationToken cancellationToken);
}
