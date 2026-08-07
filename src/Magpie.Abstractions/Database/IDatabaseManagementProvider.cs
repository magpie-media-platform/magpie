using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Database;

/// <summary>
/// The provider contract for the database engine that Magpie runs on.
/// </summary>
public interface IDatabaseManagementProvider
{
    /// <summary>
    /// The id of the database engine.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Gets whether the database accepts a connection.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the migrations that are present in the database.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyCollection<string>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default);
}
