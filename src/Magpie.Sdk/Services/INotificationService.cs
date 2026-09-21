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
/// The service contract for notification rules.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Lists the notification rules the caller can see.
    /// </summary>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<NotificationRule>> ListAsync(
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A notification rule by id when the caller can see it.
    /// </summary>
    /// <param name="id">The id of the notification rule to return</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<NotificationRule?> GetAsync(
        Guid id,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a notification rule.
    /// </summary>
    /// <param name="create">The values for the new notification rule</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<NotificationRule> CreateAsync(
        NotificationRuleCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the settings schema of a notification provider type.
    /// </summary>
    /// <param name="providerType">The slug of the provider type</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SettingSchemaDefinition>> GetSettingsSchemaAsync(
        string providerType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a notification rule.
    /// </summary>
    /// <param name="id">The id of the notification rule to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid id,
        NotificationRulePatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a notification rule.
    /// </summary>
    /// <param name="id">The id of the notification rule to delete</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid id,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates unsaved notification rule settings and sends a live test notification.
    /// </summary>
    /// <param name="rule">The unsaved notification rule to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestSettingsAsync(
        NotificationRuleCreate rule,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a test notification for a saved rule, or <see langword="null"/> when the rule is absent.
    /// </summary>
    /// <param name="id">The id of the notification rule to test</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult?> TestAsync(
        Guid id,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);
}
