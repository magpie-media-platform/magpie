using System.Collections.Generic;

using Magpie.Models;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides subject definitions.
/// </summary>
public interface ISubjectKindDefinitionProvider
{
    /// <summary>
    /// Get the subject definitions.
    /// </summary>
    IEnumerable<SubjectKindDefinition> GetSubjectKindDefinitions();
}
