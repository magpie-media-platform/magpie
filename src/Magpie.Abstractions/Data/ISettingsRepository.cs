using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Configuration;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing settings and settings groups.
/// </summary>
public interface ISettingsRepository :
    ICrudRepository<ISettingsRepository.SettingKey, Setting>,
    ICrudRepository<ISettingsRepository.SettingsGroupKey, SettingsGroup>,
    IPatchRepository<ISettingsRepository.SettingsGroupKey, SettingsGroup, SettingsGroupPatch>
{
    /// <summary>
    /// A key for a setting within a settings group for a given extension.
    /// </summary>
    /// <param name="ExtensionId">The extension ID.</param>
    /// <param name="GroupName">The settings group name.</param>
    /// <param name="Key">The setting key.</param>
    public sealed record SettingKey(Guid ExtensionId, string GroupName, string Key);
    /// <summary>
    /// A key for a settings group for a given extension.
    /// </summary>
    /// <param name="ExtensionId">The extension ID.</param>
    /// <param name="Name">The settings group name.</param>
    public sealed record SettingsGroupKey(Guid? ExtensionId, string Name);

    /// <summary>
    /// Get settings for a specific extension and group.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    /// <param name="settingsGroupName">The settings group name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Setting>>> GetSettingsAsync(
        Guid? extensionId,
        string settingsGroupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert a setting.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The setting value.</param>
    /// <param name="modifiedByUserId">The user modifying the setting.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Unit>> UpsertSettingAsync(
        SettingKey key,
        string? value,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a setting by key for a specific extension and group.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    /// <param name="groupName">The settings group name.</param>
    /// <param name="key">The setting key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<Result<Setting?>> GetSettingAsync(
        Guid extensionId,
        string groupName,
        string key,
        CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(new SettingKey(extensionId, groupName, key), cancellationToken);
    }

    /// <summary>
    /// Get all settings.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<Result<IReadOnlyList<Setting>>> GetAllSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        return ((IRepository<SettingKey, Setting>)this).GetAllAsync(cancellationToken);
    }

    /// <summary>
    /// Get a settings group by name for a specific extension.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    /// <param name="name">The settings group name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<Result<SettingsGroup?>> GetSettingsGroupAsync(
        Guid? extensionId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(new SettingsGroupKey(extensionId, name), cancellationToken);
    }

    /// <summary>
    /// Get all settings groups.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<Result<IReadOnlyList<SettingsGroup>>> GetAllSettingsGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        return ((IRepository<SettingsGroupKey, SettingsGroup>)this).GetAllAsync(cancellationToken);
    }
}
