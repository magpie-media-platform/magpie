using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Data;

namespace Magpie.Sdk.Diagnostics;

/// <summary>
/// A diagnostic repair action.
/// </summary>
public interface IDiagnosticRepair
{
    /// <summary>
    /// The repair id.
    /// </summary>
    string RepairId { get; }

    /// <summary>
    /// Runs the repair for a diagnostic finding.
    /// </summary>
    /// <param name="finding">The finding to repair</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The result of the repair.</returns>
    Task<DiagnosticRepairResult> RunAsync(
        DiagnosticFinding finding,
        CancellationToken cancellationToken = default);
}
