using System.Collections.Generic;

using Magpie.Models;

namespace Magpie.Sdk.Content;

/// <summary>
/// A source of a content type definition.
/// </summary>
public interface IContentTypeDefinitionProvider
{
    /// <summary>
    /// The content type definition.
    /// </summary>
    ContentTypeDefinition Definition { get; }

    /// <summary>
    /// Parses a file path into a search query.
    /// </summary>
    /// <param name="filePath">The file path to parse</param>
    /// <param name="levelSlug">The parsed hierarchy level slug, when any</param>
    /// <returns>The parsed signals, or <c>null</c> for a path that the provider cannot parse.</returns>
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Parse(string filePath, out string? levelSlug);
}
