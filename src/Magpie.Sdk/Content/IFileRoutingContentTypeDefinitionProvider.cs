using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Sdk.Content;

/// <summary>
/// A content type definition provider that routes files below a confirmed content match.
/// </summary>
public interface IFileRoutingContentTypeDefinitionProvider : IContentTypeDefinitionProvider
{
    /// <summary>
    /// Gets the children that match the parsed file signals.
    /// </summary>
    /// <param name="parent">The confirmed parent of the file</param>
    /// <param name="children">The direct children available for routing</param>
    /// <param name="signals">The parsed file signals</param>
    /// <returns>The child IDs in parsed file order, or an empty list when routing is unresolved</returns>
    IReadOnlyList<Guid> ResolveFileChildren(
        ContentHierarchyItem parent,
        IReadOnlyList<ContentHierarchyItem> children,
        IReadOnlyDictionary<string, List<string>> signals);
}
