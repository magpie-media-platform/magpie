using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Sdk.Diagnostics;

/// <summary>
/// A diagnostic check.
/// </summary>
public interface IDiagnosticCheck
{
    /// <summary>
    /// The stable id that this check stamps on the findings it raises.
    /// </summary>
    string DiagnosticCheckId { get; }

    /// <summary>
    /// The check category.
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Runs the check.
    /// </summary>
    /// <param name="context">The configured host entities to check</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The findings of the check.</returns>
    Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default);
}
