using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class MediaServerService(
    IMediaServerRepository mediaServerRepository,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IMaterializedMetadataRepository materializedMetadataRepository,
    IRootFolderRepository rootFolderRepository,
    IEnumerable<IMediaServerIntegration> mediaServerIntegrations,
    ISettingsService settingsService,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<MediaServerService> logger) : IMediaServerService
{
    private readonly Dictionary<string, IMediaServerIntegration> _integrations =
        BuildIntegrationMap(mediaServerIntegrations, logger);

    private static Dictionary<string, string> MergedOver(
        IReadOnlyDictionary<string, string> incoming,
        IReadOnlyDictionary<string, string>? stored)
    {
        var merged = stored is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(stored, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in incoming)
        {
            merged[entry.Key] = entry.Value;
        }

        return merged;
    }

    private static int NextConsecutiveFailures(ConfigurationTestResult result, int currentFailures) =>
        result.Success ? 0 : Math.Max(0, currentFailures) + 1;

    private static IntegrationHealthStatus ToHealthStatus(ConfigurationFailureKind failureKind, int failures)
    {
        const int unhealthyFailureThreshold = 3;
        var unhealthy = failures >= unhealthyFailureThreshold ||
            failureKind is ConfigurationFailureKind.Auth
                or ConfigurationFailureKind.Fatal
                or ConfigurationFailureKind.NotFound;
        return unhealthy ? IntegrationHealthStatus.Unhealthy : IntegrationHealthStatus.Degraded;
    }

    private static ConfigurationTestResult ToFailureResult(IntegrationSettingsValidationResult validation) =>
        new()
        {
            Success = false,
            Message = string.Join("; ", validation.Issues.Select(issue => issue.Message)),
            FailureKind = ConfigurationFailureKind.Fatal
        };

    private static async Task<ConfigurationTestResult> WithConnectionTimeoutAsync(
        Func<CancellationToken, Task<ConfigurationTestResult>> test,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = timeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource?.CancelAfter(timeout.GetValueOrDefault());

        try
        {
            var pending = test(timeoutSource?.Token ?? cancellationToken);
            return timeout is { } limit
                ? await pending.WaitAsync(limit, cancellationToken).ConfigureAwait(false)
                : await pending.ConfigureAwait(false);
        }
        catch (TimeoutException) when (timeout is not null)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = "Connection test timed out.",
                FailureKind = ConfigurationFailureKind.Transient
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
            timeoutSource is { IsCancellationRequested: true })
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = "Connection test timed out.",
                FailureKind = ConfigurationFailureKind.Transient
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = ex.Message,
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }
    }

    private static Dictionary<string, IMediaServerIntegration> BuildIntegrationMap(
        IEnumerable<IMediaServerIntegration> integrations,
        ILogger logger)
    {
        var map = new Dictionary<string, IMediaServerIntegration>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in integrations
            .Where(integration => !string.IsNullOrWhiteSpace(integration.ProviderType))
            .GroupBy(integration => integration.ProviderType, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Multiple media server integrations declared provider type {ProviderType}; the " +
                    "registry used {Winner} and ignored {Displaced}",
                    group.Key,
                    members[^1].GetType().FullName,
                    string.Join(", ", members.Take(members.Count - 1).Select(member => member.GetType().FullName)));
            }

            map[group.Key] = members[^1];
        }

        return map;
    }

    private static IReadOnlyDictionary<string, string> BuildExternalIdMap(MaterializedMetadata? metadata)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata?.ExternalIds is null)
        {
            return map;
        }

        foreach (var externalId in metadata.ExternalIds)
        {
            if (string.IsNullOrWhiteSpace(externalId.Source) || string.IsNullOrWhiteSpace(externalId.Value))
            {
                continue;
            }

            map.TryAdd(externalId.Source, externalId.Value);
        }

        return map;
    }

    private static ConfigurationTestResult Unresolved(string? providerType)
    {
        return new()
        {
            Success = false,
            Message = $"No media server integration is available for provider type {providerType ?? "(unknown)"}.",
            FailureKind = ConfigurationFailureKind.Fatal
        };
    }

    private void ValidateProviderType(string? providerType)
    {
        if (ResolveIntegration(providerType) is not null)
        {
            return;
        }

        throw new ArgumentException(
            $"No media server integration is available for provider type {providerType ?? "(unknown)"}.");
    }

    public async Task<IReadOnlyList<MediaServer>> ListAsync(CancellationToken cancellationToken = default)
    {
        return [.. await mediaServerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false)];
    }

    public async Task<MediaServer?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await mediaServerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaServer> CreateAsync(
        MediaServer server,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (string.IsNullOrWhiteSpace(server.Name))
        {
            throw new ArgumentException("Name is required.");
        }

        ValidateProviderType(server.ProviderType);

        return await mediaServerRepository.CreateAsync(server, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SettingSchemaDefinition>> GetSettingsSchemaAsync(
        string providerType,
        CancellationToken cancellationToken = default)
    {
        var integration = ResolveIntegration(providerType);

        return Task.FromResult(integration?.SettingsSchema ?? []);
    }

    public async Task<bool> PatchAsync(
        Guid id,
        MediaServerPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        if (patch.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(patch.Name.Value))
            {
                throw new ArgumentException("Name is required.");
            }
        }

        var previousStatus = patch.Status is null
            ? null
            : (await mediaServerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false))?.Status;
        var patched = await mediaServerRepository
            .PatchAsync(patch with { Id = id }, actingUserId, cancellationToken)
            .ConfigureAwait(false);

        if (patched && previousStatus is { } fromStatus && patch.Status?.Value is { } toStatus &&
            toStatus != fromStatus)
        {
            await eventBus.PublishAsync(
                IntegrationEventBuilder.BuildIntegrationHealthChangedEvent(
                    id,
                    "mediaServer",
                    fromStatus.ToString(),
                    toStatus.ToString()),
                cancellationToken).ConfigureAwait(false);
        }

        return patched;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        return await mediaServerRepository.DeleteAsync(id, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConfigurationTestResult> TestSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var existing = id is { } serverId
            ? await mediaServerRepository.GetByIdAsync(serverId, cancellationToken).ConfigureAwait(false)
            : null;

        var resolvedType = providerType ?? existing?.ProviderType;
        var integration = ResolveIntegration(resolvedType);
        if (integration is null)
        {
            return Unresolved(resolvedType);
        }

        var merged = MergedOver(settings, existing?.Settings);

        var validation = await settingsService
            .ValidateAsync(integration.SettingsSchema, merged, integration, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ToFailureResult(validation);
        }

        var transient = existing is not null
            ? existing with { ProviderType = resolvedType!, Settings = merged }
            : new MediaServer { ProviderType = resolvedType!, Name = "Connection test", Settings = merged };

        return await TestMediaServerCoreAsync(transient, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveSettingsAsync(
        Guid? id,
        IReadOnlyDictionary<string, string> overlay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        var existing = id is { } serverId
            ? await mediaServerRepository.GetByIdAsync(serverId, cancellationToken).ConfigureAwait(false)
            : null;

        return MergedOver(overlay, existing?.Settings);
    }

    public Task<ConfigurationTestResult> TestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return TestSavedAsync(id, null, cancellationToken);
    }

    public Task<ConfigurationTestResult> TestAsync(
        Guid id,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return TestSavedAsync(id, timeout, cancellationToken);
    }

    private async Task<ConfigurationTestResult> TestSavedAsync(
        Guid id,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var server = await mediaServerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = $"Media server {id} does not exist.",
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }

        var previousStatus = server.Status;
        var result = await ProbeMediaServerAsync(server, timeout, cancellationToken).ConfigureAwait(false);

        var reloaded = await mediaServerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (reloaded is not null && reloaded.Status != previousStatus)
        {
            await eventBus.PublishAsync(
                IntegrationEventBuilder.BuildIntegrationHealthChangedEvent(
                    id,
                    "mediaServer",
                    previousStatus.ToString(),
                    reloaded.Status.ToString()),
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<IReadOnlyList<MediaServerDeepLink>> GetContentLinksAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var hierarchy = await contentHierarchyItemRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (hierarchy is null)
        {
            return [];
        }

        var metadataPage = await materializedMetadataRepository.QueryAsync(
            new() { ContainerIds = [hierarchy.MetadataContainerId] },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        var metadata = metadataPage.Items.FirstOrDefault();

        var externalIds = BuildExternalIdMap(metadata);
        if (externalIds.Count == 0)
        {
            return [];
        }

        var contentType = hierarchy.ContentType;
        var rootFolder = await rootFolderRepository
            .GetByIdAsync(hierarchy.EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false);
        var rootFolderPath = rootFolder?.Path ?? string.Empty;
        var title = !string.IsNullOrWhiteSpace(metadata?.Title) ? metadata!.Title! : hierarchy.Name;

        var enabledServers = (await mediaServerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(server => server.Enabled)
            .OrderBy(server => server.Name, StringComparer.Ordinal)
            .ThenBy(server => server.Id)
            .ToArray();

        var links = new List<MediaServerDeepLink>(enabledServers.Length);
        foreach (var server in enabledServers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_integrations.TryGetValue(server.ProviderType, out var integration) ||
                integration is not ILinkableMediaServerIntegration linkable)
            {
                continue;
            }

            Uri? uri;
            try
            {
                uri = await linkable
                    .BuildItemDeepLinkAsync(server, externalIds, contentType, rootFolderPath, title, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The {ProviderType} integration failed to build a deep link for media server {MediaServerName}",
                    server.ProviderType,
                    server.Name);
                continue;
            }

            if (uri is null)
            {
                continue;
            }

            links.Add(new MediaServerDeepLink
            {
                MediaServerId = server.Id,
                ServerName = server.Name,
                ProviderType = server.ProviderType,
                Url = uri.AbsoluteUri
            });
        }

        return links;
    }

    private IMediaServerIntegration? ResolveIntegration(string? providerType)
    {
        if (providerType is null)
        {
            return null;
        }

        return _integrations.TryGetValue(providerType, out var integration) ? integration : null;
    }

    private async Task<ConfigurationTestResult> ProbeMediaServerAsync(
        MediaServer mediaServer,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var result = await TestMediaServerCoreAsync(mediaServer, timeout, cancellationToken)
            .ConfigureAwait(false);

        var failures = NextConsecutiveFailures(result, mediaServer.ConsecutiveFailures);
        var status = result.Success
            ? IntegrationHealthStatus.Healthy
            : ToHealthStatus(result.FailureKind, failures);

        await mediaServerRepository.PatchAsync(
            new MediaServerPatch
            {
                Id = mediaServer.Id,
                Status = new() { Value = status },
                LastChecked = new() { Value = timeProvider.GetUtcNow().UtcDateTime },
                LastError = new() { Value = result.Success ? null : result.Message },
                ConsecutiveFailures = new() { Value = failures },
            },
            null,
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task<ConfigurationTestResult> TestMediaServerCoreAsync(
        MediaServer mediaServer,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var integration = ResolveIntegration(mediaServer.ProviderType);
        if (integration is null)
        {
            return Unresolved(mediaServer.ProviderType);
        }

        var test = (CancellationToken ct) => integration.TestConnectionAsync(mediaServer.Settings, ct);
        return await WithConnectionTimeoutAsync(test, timeout, cancellationToken).ConfigureAwait(false);
    }
}
