using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Sdk.Content;

/// <summary>
/// A source of enrichment metadata for content hierarchy items.
/// </summary>
public interface IContentHierarchyMetadataProvider : IContentHierarchyMetadataSource
{
    /// <summary>
    /// Gets the metadata for a content hierarchy item and the definitions of its child items.
    /// </summary>
    /// <param name="request">The item and options of the metadata request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The metadata and child definitions, or <see langword="null"/> when the provider has none.</returns>
    Task<ContentHierarchyMetadataResult?> TryGetMetadataAsync(
        MetadataFetchRequest request,
        CancellationToken cancellationToken = default);
}
