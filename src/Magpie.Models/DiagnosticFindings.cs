using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// The current diagnostic findings with the time of the last scan.
/// </summary>
public sealed record DiagnosticFindings
{
    /// <summary>
    /// The findings from the most recent scan.
    /// </summary>
    public required IReadOnlyList<DiagnosticFinding> Findings { get; set; }

    /// <summary>
    /// The UTC time of the last scan, or <see langword="null"/> when no scan ran.
    /// </summary>
    public DateTime? LastScannedAt { get; set; }
}
