using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Sdk.Content;

/// <summary>
/// A content type definition provider that resolves the save path of a content file.
/// </summary>
public interface IPathResolutionContentTypeDefinitionProvider : IContentTypeDefinitionProvider
{
    /// <summary>
    /// Creates the save path.
    /// </summary>
    /// <param name="ancestors">The ancestor chain, from root to leaf.</param>
    /// <param name="relationshipsByNode">The relationship titles keyed by hierarchy item id and kind.</param>
    /// <param name="originalFileName">The file name without its path and extension.</param>
    /// <param name="extension">The content file extension.</param>
    /// <returns>The generated path, or <c>null</c> for the default path.</returns>
    string? BuildPath(
        IReadOnlyList<ContentHierarchyItem> ancestors,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        string originalFileName,
        string extension);
}
