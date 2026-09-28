using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// Selects library import candidates and their initial monitoring state.
/// </summary>
public sealed record LibraryImportSubmissionRequest : BulkAction<LibraryImportCandidateQuery>
{
    /// <summary>
    /// The required monitoring setup for imported content.
    /// </summary>
    public LibraryImportMonitoringMode? MonitoringMode { get; init; }

    /// <summary>
    /// The explicitly accepted rename preview, or null to retain existing file paths.
    /// </summary>
    public FileRenamePreview? RenamePreview { get; init; }
}
