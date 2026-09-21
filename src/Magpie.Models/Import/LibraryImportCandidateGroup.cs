using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Import;

/// <summary>
/// One top-level item that a set of library import candidates collapses into.
/// </summary>
public sealed record LibraryImportCandidateGroup
{
    private const string MatchKeyPrefix = "match:";

    private const string QueryKeyPrefix = "query:";

    /// <summary>
    /// The opaque, stable key of the group. The format of the key is not a contract.
    /// </summary>
    public required string GroupKey { get; init; }

    /// <summary>
    /// The kind of source that the group collapses.
    /// </summary>
    public LibraryImportCandidateGroupKind Kind =>
        GroupKey.StartsWith(MatchKeyPrefix, StringComparison.Ordinal)
            ? LibraryImportCandidateGroupKind.Match
            : GroupKey.StartsWith(QueryKeyPrefix, StringComparison.Ordinal)
                ? LibraryImportCandidateGroupKind.Query
                : LibraryImportCandidateGroupKind.Directory;

    /// <summary>
    /// The directory that holds the files of the group, or <see langword="null"/> when it is not one directory.
    /// </summary>
    public string? SourcePath => Kind == LibraryImportCandidateGroupKind.Directory ? GroupKey : null;

    /// <summary>
    /// The display title of the group.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The slug of the content type of the group's candidates.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// The secondary title of the group, or <see langword="null"/> when there is none.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// The metadata match of the group, or <see langword="null"/> when it is not yet matched.
    /// </summary>
    public LibraryImportMatch? Match { get; init; }

    /// <summary>
    /// The number of candidates the group collapses.
    /// </summary>
    public required int LeafCount { get; init; }

    /// <summary>
    /// The slug of the hierarchy level of the group.
    /// </summary>
    public required string TopLevelSlug { get; init; }

    /// <summary>
    /// The slug of the leaf hierarchy level the group's candidates fulfil.
    /// </summary>
    public required string LeafLevelSlug { get; init; }

    /// <summary>
    /// The display name of the leaf hierarchy level, from the content type definition.
    /// </summary>
    public required string LeafLevelDisplayName { get; init; }

    /// <summary>
    /// The slug of the hierarchy level directly below <see cref="TopLevelSlug"/>.
    /// </summary>
    public string? ChildLevelSlug { get; init; }

    /// <summary>
    /// The display name of <see cref="ChildLevelSlug"/>.
    /// </summary>
    public string? ChildLevelDisplayName { get; init; }

    /// <summary>
    /// The number of candidates in the group at each status.
    /// </summary>
    public required IReadOnlyDictionary<LibraryImportCandidateStatus, int> StatusCounts { get; init; }
}
