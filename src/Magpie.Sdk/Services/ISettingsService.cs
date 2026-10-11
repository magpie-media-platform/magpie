using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for system and extension settings groups.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Lists the settings groups with their declared schema.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SettingsGroupDetail>> ListGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A system settings group by name.
    /// </summary>
    /// <param name="name">The name of the settings group</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<SettingsGroupDetail?> GetSystemGroupAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The trimmed name of this instance, or "Magpie" when no name is set.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<string> GetInstanceNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// An extension settings group by extension id and name.
    /// </summary>
    /// <param name="extensionId">The id of the extension that owns the group</param>
    /// <param name="name">The name of the settings group</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<SettingsGroupDetail?> GetExtensionGroupAsync(
        Guid extensionId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a system settings group.
    /// </summary>
    /// <param name="name">The name of the settings group</param>
    /// <param name="patch">The setting values to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchSystemGroupAsync(
        string name,
        SettingsGroupPatch patch,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches an extension settings group.
    /// </summary>
    /// <param name="extensionId">The id of the extension that owns the group</param>
    /// <param name="name">The name of the settings group</param>
    /// <param name="patch">The setting values to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchExtensionGroupAsync(
        Guid extensionId,
        string name,
        SettingsGroupPatch patch,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the connection test of an extension settings group with candidate settings.
    /// </summary>
    /// <param name="extensionId">The id of the extension that owns the group</param>
    /// <param name="name">The name of the settings group</param>
    /// <param name="settings">The candidate settings to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult?> TestGroupAsync(
        Guid extensionId,
        string name,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates settings against a schema and the validator of an integration.
    /// </summary>
    /// <param name="schema">The schema that the settings must satisfy</param>
    /// <param name="settings">The candidate settings to validate</param>
    /// <param name="integration">The integration instance that supplies the validator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IntegrationSettingsValidationResult> ValidateAsync(
        IReadOnlyList<SettingSchemaDefinition> schema,
        IReadOnlyDictionary<string, string> settings,
        object integration,
        CancellationToken cancellationToken = default);
}
