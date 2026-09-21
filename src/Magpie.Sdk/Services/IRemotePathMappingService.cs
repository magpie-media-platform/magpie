using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for remote path mappings.
/// </summary>
public interface IRemotePathMappingService
{
    /// <summary>
    /// Lists the remote path mappings.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<RemotePathMapping>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A remote path mapping by id.
    /// </summary>
    /// <param name="id">The id of the mapping to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<RemotePathMapping?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a remote path mapping.
    /// </summary>
    /// <param name="create">The values for the new mapping</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<RemotePathMapping> CreateAsync(
        RemotePathMappingCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a remote path mapping.
    /// </summary>
    /// <param name="id">The id of the mapping to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid id,
        RemotePathMappingPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a remote path mapping.
    /// </summary>
    /// <param name="id">The id of the mapping to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests whether the Magpie host can read a local path.
    /// </summary>
    /// <param name="localPath">The local directory to test</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestLocalPathAsync(
        string localPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the host from download client settings.
    /// </summary>
    /// <param name="settings">The stored settings of the download client</param>
    string? GetHost(IReadOnlyDictionary<string, string>? settings);

    /// <summary>
    /// Maps a remote path to a local path.
    /// </summary>
    /// <param name="remotePath">The path that the download client reports</param>
    /// <param name="clientHost">The host of the download client, or <see langword="null"/> when unknown</param>
    /// <param name="mappings">The available mappings</param>
    string Map(string remotePath, string? clientHost, IEnumerable<RemotePathMapping> mappings);
}
