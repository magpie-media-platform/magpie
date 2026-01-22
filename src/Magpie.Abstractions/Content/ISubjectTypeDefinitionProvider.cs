using System.Collections.Generic;

using Magpie.Models.Content;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides subject type definitions.
/// </summary>
public interface ISubjectTypeDefinitionProvider
{
    /// <summary>
    /// Gets the subject type definitions.
    /// </summary>
    IReadOnlyList<SubjectTypeDefinition> GetSubjectTypeDefinitions();
}
