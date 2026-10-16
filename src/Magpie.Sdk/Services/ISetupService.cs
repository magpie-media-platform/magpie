using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Sdk.Services;

/// <summary>
/// The management of first-run setup.
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// The id of the configured database provider.
    /// </summary>
    string DatabaseProvider { get; }

    /// <summary>
    /// Reports whether setup is complete.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether an enabled local administrator exists.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> HasLocalAdministratorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether the database is available.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the applied database migrations.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyCollection<string>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the database configuration.
    /// </summary>
    /// <param name="provider">The database provider id</param>
    /// <param name="connectionString">The connection string, or <see langword="null"/> when unused</param>
    /// <param name="roles">The roles of the node, or <see langword="null"/> for a new installation</param>
    /// <param name="jobTypes">The job types that the node runs, or empty for every job type</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DatabaseSetupResult> ConfigureDatabaseAsync(
        string provider,
        string? connectionString,
        IReadOnlyList<NodeRole>? roles = null,
        IReadOnlyList<string>? jobTypes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the first local administrator.
    /// </summary>
    /// <param name="username">The username</param>
    /// <param name="password">The plaintext password</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<User> CreateAdministratorAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks setup as complete.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
