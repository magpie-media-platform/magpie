using System.Collections.Generic;

using Magpie.Models;

namespace Magpie.Sdk.Content;

/// <summary>
/// A source of subject kind definitions.
/// </summary>
public interface ISubjectKindDefinitionProvider
{
    /// <summary>
    /// Gets the subject kind definitions.
    /// </summary>
    IEnumerable<SubjectKindDefinition> GetSubjectKindDefinitions();
}
