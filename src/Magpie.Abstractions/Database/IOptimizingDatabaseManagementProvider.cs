using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Database;

/// <summary>
/// The provider contract for database optimization.
/// </summary>
public interface IOptimizingDatabaseManagementProvider : IDatabaseManagementProvider
{
    /// <summary>
    /// Gets the time of the last optimization that succeeded.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The time of the last optimization, or null when no optimization ran</returns>
    Task<DateTime?> GetLastOptimizedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes the database to reclaim disk space.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}
