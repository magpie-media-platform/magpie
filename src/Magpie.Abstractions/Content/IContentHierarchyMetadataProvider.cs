using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides metadata for content hierarchy items from an external source.
/// </summary>
public interface IContentHierarchyMetadataProvider
{
    /// <summary>
    /// The unique type identifier for the provider.
    /// </summary>
    public string ProviderType { get; }

    /// <summary>
    /// The content type supported by the provider.
    /// </summary>
    IEnumerable<string> SupportedContentTypes { get; }

    /// <summary>
    /// Fetch metadata for a content hierarchy level.
    /// </summary>
    /// <param name="contentHierarchyItem">The content hierarchy item to provide metadata for.</param>
    /// <param name="existingMetadata">Any existing metadata contributions that exist for the item.</param>
    /// <param name="ancestorLanguagePreferences">The language preferences of the nearest ancestor.</param>
    /// <param name="defaultLanguagePreference">The default language preference for the system.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<MetadataContribution?> TryGetMetadataAsync(
        ContentHierarchyItem contentHierarchyItem,
        IEnumerable<MetadataContribution> existingMetadata,
        IEnumerable<string>? ancestorLanguagePreferences,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);
}
