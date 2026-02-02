using System.Collections.Generic;

using Magpie.Models;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides content type definitions.
/// </summary>
public interface IContentTypeDefinitionProvider
{
    /// <summary>
    ///  List custom content type definitions.
    /// </summary>
    IEnumerable<ContentTypeDefinition> GetContentTypeDefinitions();
}
