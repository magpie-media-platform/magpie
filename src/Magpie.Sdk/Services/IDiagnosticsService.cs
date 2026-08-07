using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for diagnostic findings and their repairs.
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>
    /// Gets the current findings and the time of the last scan.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DiagnosticFindings> GetFindingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the repair that the server resolves from a finding.
    /// </summary>
    /// <param name="findingId">The id of the finding to repair</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DiagnosticRepairResult> RepairAsync(
        Guid findingId,
        CancellationToken cancellationToken = default);
}
