using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A finding from a diagnostic check.
/// </summary>
public sealed record DiagnosticFinding
{
    /// <summary>
    /// The unique id of the diagnostic finding.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the diagnostic check that made the finding.
    /// </summary>
    public required string DiagnosticCheckId { get; set; }

    /// <summary>
    /// The severity of the finding.
    /// </summary>
    public DiagnosticFindingSeverity Severity { get; set; }

    /// <summary>
    /// The title of the finding.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The detail text of the finding.
    /// </summary>
    public required string Detail { get; set; }

    /// <summary>
    /// The entity that the finding affects.
    /// </summary>
    public string? AffectedEntity { get; set; }

    /// <summary>
    /// The id of the repair that corrects the finding.
    /// </summary>
    public string? RepairId { get; set; }

    /// <summary>
    /// The UTC time of the detection.
    /// </summary>
    public DateTime DetectedAt { get; set; }
}
