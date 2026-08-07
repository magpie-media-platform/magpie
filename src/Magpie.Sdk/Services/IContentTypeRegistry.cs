using System.Collections.Generic;

using Magpie.Models;
using Magpie.Sdk.Content;

namespace Magpie.Sdk.Services;

/// <summary>
/// The registry of content type definitions, keyed by slug.
/// </summary>
public interface IContentTypeRegistry
{
    /// <summary>
    /// The registered content type definitions.
    /// </summary>
    IReadOnlyList<ContentTypeDefinition> Definitions { get; }

    /// <summary>
    /// Gets the content type definition with a slug.
    /// </summary>
    /// <param name="levelSlug">The slug of the content type to return</param>
    /// <returns>The matching definition, or <see langword="null"/> when none is registered</returns>
    ContentTypeDefinition? GetDefinitionFor(string levelSlug);

    /// <summary>
    /// Gets the registered provider for a content type.
    /// </summary>
    /// <param name="contentType">The slug of the content type</param>
    IContentTypeDefinitionProvider? FindProviderFor(string contentType);

    /// <summary>
    /// Gets the root hierarchy level slug for a content type.
    /// </summary>
    /// <param name="contentType">The slug of the content type</param>
    /// <param name="levelSlug">The slug of the wanted root level, or <see langword="null"/> for the default</param>
    string GetRootLevelSlug(string contentType, string? levelSlug);

    /// <summary>
    /// Lists the root hierarchy levels of a content type.
    /// </summary>
    /// <param name="contentType">The slug of the content type</param>
    IReadOnlyList<ContentHierarchyLevelDefinition> GetRootLevels(string contentType);

    /// <summary>
    /// Lists the hierarchy levels declared under one level of a content type.
    /// </summary>
    /// <param name="contentType">The slug of the content type</param>
    /// <param name="levelSlug">The slug of the parent level</param>
    IReadOnlyList<ContentHierarchyLevelDefinition> GetChildLevels(string contentType, string levelSlug);

    /// <summary>
    /// Reports whether a content type with a slug is registered.
    /// </summary>
    /// <param name="levelSlug">The slug of the content type to test</param>
    /// <returns><see langword="true"/> when a matching definition is registered</returns>
    bool Exists(string levelSlug);
}
