using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions.Database;

/// <summary>
/// The provider contract for database engines that validate startup.
/// </summary>
public interface IStartupValidationDatabaseManagementProvider : IDatabaseManagementProvider
{
    /// <summary>
    /// Validates that database startup can continue.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DatabaseStartupValidationResult> ValidateStartupAsync(CancellationToken cancellationToken = default);
}
