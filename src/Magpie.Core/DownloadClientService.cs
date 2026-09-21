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
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class DownloadClientService(
    IDownloadClientRepository downloadClientRepository,
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    ISettingsService settingsService,
    IEventBus eventBus,
    TimeProvider timeProvider) : IDownloadClientService
{
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

    private static ConfigurationTestResult Unresolved(string? providerType)
    {
        return new()
        {
            Success = false,
            Message = $"No download client integration is available for provider type {providerType ?? "(unknown)"}.",
            FailureKind = ConfigurationFailureKind.Fatal
        };
    }

    public async Task<IReadOnlyList<DownloadClient>> ListAsync(CancellationToken cancellationToken = default)
    {
        var clients = await downloadClientRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. clients];
    }

    public Task<DownloadClient?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return downloadClientRepository.GetByIdAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<SettingSchemaDefinition>> GetSettingsSchemaAsync(
        string providerType,
        CancellationToken cancellationToken = default)
    {
        var integration = downloadClientIntegrations.LastOrDefault(candidate => string.Equals(
            candidate.ProviderType,
            providerType,
            StringComparison.Ordinal));
        return Task.FromResult(integration?.SettingsSchema ?? []);
    }

    public async Task<DownloadClient> CreateAsync(
        DownloadClient client,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await downloadClientRepository.CreateAsync(client, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PatchAsync(
        Guid id,
        DownloadClientPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var previousStatus = patch.Status is null
            ? null
            : (await downloadClientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false))?.Status;
        var patched = await downloadClientRepository
            .PatchAsync(patch with { Id = id }, actingUserId, cancellationToken)
            .ConfigureAwait(false);

        if (patched && previousStatus is { } fromStatus && patch.Status?.Value is { } toStatus &&
            toStatus != fromStatus)
        {
            await eventBus.PublishAsync(
                IntegrationEventBuilder.BuildIntegrationHealthChangedEvent(
                    id, "downloadClient", fromStatus.ToString(), toStatus.ToString()),
                cancellationToken).ConfigureAwait(false);
        }

        return patched;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        return await downloadClientRepository.DeleteAsync(id, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConfigurationTestResult> TestSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var existing = id is { } clientId
            ? await downloadClientRepository.GetByIdAsync(clientId, cancellationToken).ConfigureAwait(false)
            : null;

        var resolvedType = providerType ?? existing?.ProviderType;
        var integration = resolvedType is null
            ? null
            : downloadClientIntegrations.LastOrDefault(
                candidate => string.Equals(candidate.ProviderType, resolvedType, StringComparison.Ordinal));
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
            : new DownloadClient { ProviderType = resolvedType!, Name = "Connection test", Settings = merged };

        return await TestDownloadClientCoreAsync(transient, null, cancellationToken).ConfigureAwait(false);
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
        var client = await downloadClientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = $"Download client {id} does not exist.",
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }

        var previousStatus = client.Status;
        var result = await ProbeDownloadClientAsync(client, timeout, cancellationToken).ConfigureAwait(false);

        var reloaded = await downloadClientRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (reloaded is not null && reloaded.Status != previousStatus)
        {
            await eventBus.PublishAsync(
                IntegrationEventBuilder.BuildIntegrationHealthChangedEvent(
                    id,
                    "downloadClient",
                    previousStatus.ToString(),
                    reloaded.Status.ToString()),
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<ConfigurationTestResult> ProbeDownloadClientAsync(
        DownloadClient downloadClient,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var result = await TestDownloadClientCoreAsync(downloadClient, timeout, cancellationToken)
            .ConfigureAwait(false);

        var failures = NextConsecutiveFailures(result, downloadClient.ConsecutiveFailures);
        var status = result.Success
            ? IntegrationHealthStatus.Healthy
            : ToHealthStatus(result.FailureKind, failures);

        await downloadClientRepository.PatchAsync(
            new DownloadClientPatch
            {
                Id = downloadClient.Id,
                Status = new() { Value = status },
                LastChecked = new() { Value = timeProvider.GetUtcNow().UtcDateTime },
                LastError = new() { Value = result.Success ? null : result.Message },
                ConsecutiveFailures = new() { Value = failures },
            },
            null,
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task<ConfigurationTestResult> TestDownloadClientCoreAsync(
        DownloadClient downloadClient,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var integration = downloadClientIntegrations.LastOrDefault(
            candidate => string.Equals(candidate.ProviderType, downloadClient.ProviderType, StringComparison.Ordinal));
        if (integration is null)
        {
            return Unresolved(downloadClient.ProviderType);
        }

        var test = (CancellationToken ct) => integration.TestConnectionAsync(downloadClient.Settings, ct);
        return await WithConnectionTimeoutAsync(test, timeout, cancellationToken).ConfigureAwait(false);
    }
}
