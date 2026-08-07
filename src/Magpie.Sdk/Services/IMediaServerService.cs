using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Integrations;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for media servers and their content deep links.
/// </summary>
public interface IMediaServerService
{
    /// <summary>
    /// Lists the configured media servers.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<MediaServer>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a media server by id.
    /// </summary>
    /// <param name="id">The id of the media server to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MediaServer?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a media server.
    /// </summary>
    /// <param name="server">The values for the new media server</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MediaServer> CreateAsync(
        MediaServer server,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the settings schema of a media server provider type.
    /// </summary>
    /// <param name="providerType">The slug of the provider type</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SettingSchemaDefinition>> GetSettingsSchemaAsync(
        string providerType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a media server.
    /// </summary>
    /// <param name="id">The id of the media server to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid id,
        MediaServerPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a media server.
    /// </summary>
    /// <param name="id">The id of the media server to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid id,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates unsaved media server settings and runs any live connection test the provider supports.
    /// </summary>
    /// <param name="providerType">The provider type slug, or <see langword="null"/> to use the saved server</param>
    /// <param name="id">The id of the saved server that supplies stored secrets, or <see langword="null"/></param>
    /// <param name="settings">The candidate settings to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the effective settings of a media server, secrets included.
    /// </summary>
    /// <param name="id">The id of the saved server, or <see langword="null"/> when none applies</param>
    /// <param name="overlay">The partial settings that win over the stored settings</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyDictionary<string, string>> ResolveSettingsAsync(
        Guid? id,
        IReadOnlyDictionary<string, string> overlay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the live connection test of a saved media server.
    /// </summary>
    /// <param name="id">The id of the media server to test</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the live connection test of a saved media server within a time limit.
    /// </summary>
    /// <param name="id">The id of the media server to test</param>
    /// <param name="timeout">The maximum test duration</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestAsync(
        Guid id,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the media server deep links for a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item to link to</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<MediaServerDeepLink>> GetContentLinksAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);
}
