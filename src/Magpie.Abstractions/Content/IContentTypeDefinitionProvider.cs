using System.Collections.Generic;

using Magpie.Models.Content;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides content type definitions.
/// </summary>
public interface IContentTypeDefinitionProvider
{
    /// <summary>
    /// Gets the content type definitions.
    /// </summary>
    IReadOnlyList<ContentTypeDefinition> GetContentTypeDefinitions();
}
