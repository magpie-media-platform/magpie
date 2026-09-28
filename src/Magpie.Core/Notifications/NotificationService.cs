using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Repositories;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Notifications;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Notifications;

internal sealed class NotificationService(
    INotificationRuleRepository rules,
    IEnumerable<INotificationProvider> providers,
    ISettingsService settingsService,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task<IReadOnlyList<NotificationRule>> ListAsync(
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var all = await rules.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return callerIsAdmin
            ? [.. all]
            : [.. all.Where(rule => rule.OwnerUserId == callerUserId)];
    }

    public async Task<NotificationRule?> GetAsync(
        Guid id,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var rule = await rules.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (rule is null || callerIsAdmin)
        {
            return rule;
        }

        return rule.OwnerUserId == callerUserId ? rule : null;
    }

    public async Task<NotificationRule> CreateAsync(
        NotificationRuleCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var rule = new NotificationRule
        {
            Id = Guid.Empty,
            Name = create.Name,
            OwnerUserId = create.OwnerUserId,
            EventFilter = create.EventFilter,
            ProviderType = create.ProviderType,
            Settings = create.Settings is null
                ? []
                : new Dictionary<string, string>(create.Settings),
            Enabled = create.Enabled,
        };

        if (FindProvider(rule.ProviderType) is { } provider &&
            await ValidateSettingsAsync(provider, rule.Settings, cancellationToken).ConfigureAwait(false) is { } issues)
        {
            throw new MagpieException(issues);
        }

        var created = await rules.CreateAsync(rule, actingUserId, cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<bool> PatchAsync(
        Guid id,
        NotificationRulePatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var rule = await GetAsync(id, callerUserId, callerIsAdmin, cancellationToken).ConfigureAwait(false);
        if (rule is null)
        {
            return false;
        }

        var settings = new Dictionary<string, string>(rule.Settings);
        if (patch.Settings is not null)
        {
            NotificationRuleRepository.ApplyPatch(settings, patch.Settings);
        }

        if (FindProvider(rule.ProviderType) is { } provider &&
            await ValidateSettingsAsync(provider, settings, cancellationToken).ConfigureAwait(false) is { } issues)
        {
            throw new MagpieException(issues);
        }

        return await rules.PatchAsync(patch with { Id = id }, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var rule = await GetAsync(id, callerUserId, callerIsAdmin, cancellationToken).ConfigureAwait(false);

        return rule is not null &&
            await rules.DeleteAsync(id, callerUserId, cancellationToken).ConfigureAwait(false);
    }

    public Task<ConfigurationTestResult> TestSettingsAsync(
        NotificationRuleCreate rule,
        CancellationToken cancellationToken = default)
    {
        var transient = new NotificationRule
        {
            Id = Guid.Empty,
            Name = rule.Name,
            OwnerUserId = rule.OwnerUserId,
            EventFilter = rule.EventFilter,
            ProviderType = rule.ProviderType,
            Settings = rule.Settings is null
                ? []
                : new Dictionary<string, string>(rule.Settings),
            Enabled = rule.Enabled,
        };

        return DispatchTestAsync(transient, cancellationToken);
    }

    public async Task<ConfigurationTestResult?> TestAsync(
        Guid id,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var rule = await GetAsync(id, callerUserId, callerIsAdmin, cancellationToken).ConfigureAwait(false);
        if (rule is null)
        {
            return null;
        }

        return await DispatchTestAsync(rule, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConfigurationTestResult> DispatchTestAsync(
        NotificationRule rule,
        CancellationToken cancellationToken)
    {
        var provider = FindProvider(rule.ProviderType);
        if (provider is null)
        {
            logger.LogWarning(
                "The notification service refused the test for rule {NotificationRuleId} because " +
                "provider {ProviderType} is not registered",
                rule.Id,
                rule.ProviderType);

            return new ConfigurationTestResult
            {
                Success = false,
                Message = $"No notification integration is available for provider type {rule.ProviderType}.",
                FailureKind = ConfigurationFailureKind.Fatal,
            };
        }

        if (await ValidateSettingsAsync(provider, rule.Settings, cancellationToken).ConfigureAwait(false) is { } issues)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = "The test was not sent: " + issues,
                FailureKind = ConfigurationFailureKind.Fatal,
            };
        }

        var syntheticEvent = NotificationEventBuilder.BuildNotificationTestEvent(rule.Id, rule.Name, test: true);

        try
        {
            await provider.SendAsync(rule, syntheticEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The notification service failed to send the test message for rule {NotificationRuleId}",
                rule.Id);

            return new ConfigurationTestResult
            {
                Success = false,
                Message = ex.Message,
                FailureKind = ConfigurationFailureKind.Fatal,
            };
        }

        return new ConfigurationTestResult
        {
            Success = true,
            Message = "Test notification sent to the provider.",
            FailureKind = ConfigurationFailureKind.None,
        };
    }

    private async Task<string?> ValidateSettingsAsync(
        INotificationProvider provider,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        var validation = await settingsService
            .ValidateAsync(provider.SettingsSchema, settings, provider, cancellationToken)
            .ConfigureAwait(false);

        return validation.IsValid ? null : string.Join(" ", validation.Issues.Select(issue => issue.Message));
    }

    private INotificationProvider? FindProvider(string providerType)
    {
        if (string.IsNullOrWhiteSpace(providerType))
        {
            return null;
        }

        return providers.FirstOrDefault(
            provider => string.Equals(provider.ProviderType, providerType, StringComparison.Ordinal));
    }

    public Task<IReadOnlyList<SettingSchemaDefinition>> GetSettingsSchemaAsync(
        string providerType,
        CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(providerType);

        return Task.FromResult(provider?.SettingsSchema ?? []);
    }
}
