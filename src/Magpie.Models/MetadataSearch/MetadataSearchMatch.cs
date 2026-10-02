using System;

namespace Magpie.Models.MetadataSearch;

/// <summary>
/// A provider candidate with its local library state.
/// </summary>
public sealed record MetadataSearchMatch
{
    /// <summary>
    /// The provider candidate.
    /// </summary>
    public required MetadataSearchCandidate Candidate { get; init; }

    /// <summary>
    /// Whether the candidate exists in the library.
    /// </summary>
    public required bool InLibrary { get; init; }

    /// <summary>
    /// The id of the existing content hierarchy item.
    /// </summary>
    public Guid? ExistingContentHierarchyItemId { get; init; }
}
