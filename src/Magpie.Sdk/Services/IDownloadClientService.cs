using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for download clients.
/// </summary>
public interface IDownloadClientService
{
    /// <summary>
    /// Lists the configured download clients.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<DownloadClient>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A download client by id.
    /// </summary>
    /// <param name="id">The id of the download client to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DownloadClient?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the settings schema of a download client provider type.
    /// </summary>
    /// <param name="providerType">The slug of the provider type</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SettingSchemaDefinition>> GetSettingsSchemaAsync(
        string providerType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a download client.
    /// </summary>
    /// <param name="client">The values for the new download client</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DownloadClient> CreateAsync(
        DownloadClient client,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a download client.
    /// </summary>
    /// <param name="id">The id of the download client to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid id,
        DownloadClientPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a download client.
    /// </summary>
    /// <param name="id">The id of the download client to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid id,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates unsaved download client settings and runs any live connection test the provider supports.
    /// </summary>
    /// <param name="providerType">The provider type slug, or <see langword="null"/> to use the saved client</param>
    /// <param name="id">The id of the saved client that supplies stored secrets, or <see langword="null"/></param>
    /// <param name="settings">The candidate settings to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the live connection test of a saved download client.
    /// </summary>
    /// <param name="id">The id of the download client to test</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the live connection test of a saved download client within a time limit.
    /// </summary>
    /// <param name="id">The id of the download client to test</param>
    /// <param name="timeout">The maximum test duration</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestAsync(
        Guid id,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
