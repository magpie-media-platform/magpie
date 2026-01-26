using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing server configuration.
/// </summary>
/// <remarks>
/// ServerConfig is a singleton entity -- there is only one configuration record.
/// </remarks>
public interface IServerConfigRepository
{
    /// <summary>
    /// Get the current server configuration.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<ServerConfig?>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the server configuration.
    /// </summary>
    /// <param name="model">The updated configuration.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the configuration was updated,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> UpdateAsync(
        ServerConfig model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patch the server configuration with partial updates.
    /// </summary>
    /// <param name="patch">The patch object containing the updates.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the configuration was patched,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> PatchAsync(
        ServerConfigPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);
}
