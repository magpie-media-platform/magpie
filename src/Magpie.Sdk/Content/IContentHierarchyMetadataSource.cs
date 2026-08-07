using System.Collections.Generic;

namespace Magpie.Sdk.Content;

/// <summary>
/// The identity of a content hierarchy metadata source.
/// </summary>
public interface IContentHierarchyMetadataSource
{
    /// <summary>
    /// The metadata provider type.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// The supported content type slugs.
    /// </summary>
    IEnumerable<string> SupportedContentTypes { get; }
}
