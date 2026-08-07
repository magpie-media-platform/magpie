using System.Collections.Generic;

using Magpie.Models;

namespace Magpie.Sdk.Services;

/// <summary>
/// The registry of subject kind definitions, keyed by slug.
/// </summary>
public interface ISubjectKindRegistry
{
    /// <summary>
    /// The registered subject kind definitions.
    /// </summary>
    IReadOnlyList<SubjectKindDefinition> Definitions { get; }

    /// <summary>
    /// Reports whether a subject kind with a slug is registered.
    /// </summary>
    /// <param name="slug">The slug of the subject kind to test</param>
    /// <returns><see langword="true"/> when a matching definition is registered</returns>
    bool Contains(string slug);
}
