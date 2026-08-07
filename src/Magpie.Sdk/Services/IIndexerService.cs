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
/// The service contract for indexer connections and feeds.
/// </summary>
public interface IIndexerService
{
    /// <summary>
    /// Lists the configured indexers.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Indexer>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an indexer by id.
    /// </summary>
    /// <param name="id">The id of the indexer to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Indexer?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an indexer.
    /// </summary>
    /// <param name="create">The values for the new indexer</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Indexer> CreateAsync(
        IndexerCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches an indexer.
    /// </summary>
    /// <param name="id">The id of the indexer to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid id,
        IndexerPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an indexer.
    /// </summary>
    /// <param name="id">The id of the indexer to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid id,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the settings schema of one indexer instance.
    /// </summary>
    /// <param name="id">The id of the indexer to read the schema of</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SettingSchemaDefinition>> GetSettingsSchemaAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates unsaved indexer settings and runs any live connection test the provider supports.
    /// </summary>
    /// <param name="providerType">The provider type slug, or <see langword="null"/> to use the saved indexer</param>
    /// <param name="id">The id of the saved indexer that supplies stored secrets, or <see langword="null"/></param>
    /// <param name="settings">The candidate settings to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the live connection test of a saved indexer.
    /// </summary>
    /// <param name="id">The id of the indexer to test</param>
    /// <param name="connectionId">The id of the only connection to test, or <see langword="null"/> for all</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestAsync(
        Guid id,
        Guid? connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the connections configured on an indexer.
    /// </summary>
    /// <param name="indexerId">The id of the indexer that owns the connections</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<IndexerConnection>> ListConnectionsAsync(
        Guid indexerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a connection on an indexer.
    /// </summary>
    /// <param name="indexerId">The id of the indexer that owns the connection</param>
    /// <param name="create">The values for the new connection</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IndexerConnection> CreateConnectionAsync(
        Guid indexerId,
        IndexerConnectionCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches an indexer connection.
    /// </summary>
    /// <param name="indexerId">The id of the indexer that owns the connection</param>
    /// <param name="connectionId">The id of the connection to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        IndexerConnectionPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an indexer connection.
    /// </summary>
    /// <param name="indexerId">The id of the indexer that owns the connection</param>
    /// <param name="connectionId">The id of the connection to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the live connection test of a saved indexer connection.
    /// </summary>
    /// <param name="indexerId">The id of the indexer that owns the connection</param>
    /// <param name="connectionId">The id of the connection to test</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the live connection test of a saved indexer connection within a time limit.
    /// </summary>
    /// <param name="indexerId">The id of the indexer that owns the connection</param>
    /// <param name="connectionId">The id of the connection to test</param>
    /// <param name="timeout">The maximum test duration</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the configured indexer feeds.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<IndexerFeed>> ListFeedsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the settings schema of an indexer feed provider type.
    /// </summary>
    /// <param name="providerType">The slug of the feed provider type</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SettingSchemaDefinition>> GetFeedSettingsSchemaAsync(
        string providerType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an indexer feed.
    /// </summary>
    /// <param name="feed">The values for the new feed</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IndexerFeed> CreateFeedAsync(
        IndexerFeed feed,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches an indexer feed.
    /// </summary>
    /// <param name="feedId">The id of the feed to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchFeedAsync(
        Guid feedId,
        IndexerFeedPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an indexer feed.
    /// </summary>
    /// <param name="feedId">The id of the feed to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteFeedAsync(
        Guid feedId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates unsaved indexer feed settings and runs any live connection test the provider supports.
    /// </summary>
    /// <param name="providerType">The provider type slug, or <see langword="null"/> to use the saved feed</param>
    /// <param name="id">The id of the saved feed that supplies stored secrets, or <see langword="null"/></param>
    /// <param name="settings">The candidate settings to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestFeedSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the live connection test of a saved indexer feed.
    /// </summary>
    /// <param name="feedId">The id of the feed to test</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestFeedAsync(Guid feedId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the stored indexer rows of a feed with the discovered set.
    /// </summary>
    /// <param name="feed">The feed that owns the indexer rows</param>
    /// <param name="discovered">The indexers the feed reports now</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task SyncFeedIndexersAsync(
        IndexerFeed feed,
        IReadOnlyList<Indexer> discovered,
        Guid? userId,
        CancellationToken cancellationToken = default);
}
