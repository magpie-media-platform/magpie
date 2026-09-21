using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A file that a library scan found for import.
/// </summary>
public record LibraryImportCandidate
{
    /// <summary>
    /// The unique id of the library import candidate.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the root folder.
    /// </summary>
    public required Guid RootFolderId { get; set; }

    /// <summary>
    /// The id of the scan run.
    /// </summary>
    public required Guid ScanRunId { get; set; }

    /// <summary>
    /// The path of the file to import.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// The slug of the content type.
    /// </summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// The status of the import candidate.
    /// </summary>
    public required LibraryImportCandidateStatus Status { get; set; }

    /// <summary>
    /// The signals parsed from the file path, keyed by name.
    /// </summary>
    public Dictionary<string, List<string>> ParsedSignals { get; set; } = [];

    /// <summary>
    /// The absolute path of the top-level directory the scan found this candidate under.
    /// </summary>
    public string? LayoutKey { get; set; }

    /// <summary>
    /// The key of the candidate group this candidate collapses into, derived by the repository on every write.
    /// </summary>
    public string? GroupKey { get; set; }

    /// <summary>
    /// The metadata matches for this import candidate.
    /// </summary>
    public List<LibraryImportMatch> Matches { get; set; } = [];

    /// <summary>
    /// The number of metadata matches.
    /// </summary>
    public int MatchCount { get; set; }

    /// <summary>
    /// The notes about the import candidate.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
