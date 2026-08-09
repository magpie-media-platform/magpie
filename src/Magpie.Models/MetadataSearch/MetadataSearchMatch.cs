using System;

namespace Magpie.Models.MetadataSearch;

/// <summary>
/// A provider candidate with its local library state.
/// </summary>
public sealed record MetadataSearchMatch
{
    /// <summary>
    /// Provider candidate.
    /// </summary>
    public required MetadataSearchCandidate Candidate { get; init; }

    /// <summary>
    /// Whether the candidate exists in the library.
    /// </summary>
    public required bool InLibrary { get; init; }

    /// <summary>
    /// Existing content hierarchy item identifier.
    /// </summary>
    public Guid? ExistingContentHierarchyItemId { get; init; }
}
