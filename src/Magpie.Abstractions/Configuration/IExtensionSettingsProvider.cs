using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

namespace Magpie.Abstractions.Configuration;

/// <summary>
/// Provides access to extension-specific settings. Scoped to a single extension via keyed DI.
/// </summary>
public interface IExtensionSettingsProvider
{
    /// <summary>
    /// The extension identifier.
    /// </summary>
    Guid ExtensionId { get; }

    /// <summary>
    /// Gets a setting value by key from a settings group with a default value.
    /// </summary>
    /// <param name="settingsGroupName">The settings group name.</param>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The default value to return if the key does not exist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The setting value, or the default value if the key does not exist.</returns>
    Task<Result<string?>> GetSettingAsync(
        string settingsGroupName,
        string key,
        string? defaultValue = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all settings from a settings group.
    /// </summary>
    /// <param name="settingsGroupName">The settings group name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary of all setting key-value pairs.</returns>
    Task<Result<IReadOnlyDictionary<string, string>>> GetSettingsAsync(
        string settingsGroupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a setting value in a settings group.
    /// </summary>
    /// <param name="settingsGroupName">The settings group name.</param>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The setting value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Unit>> SetSettingAsync(
        string settingsGroupName,
        string key,
        string value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a setting by key from a settings group.
    /// </summary>
    /// <param name="settingsGroupName">The settings group name.</param>
    /// <param name="key">The setting key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the setting was deleted, false if it did not exist.</returns>
    Task<Result<bool>> DeleteSettingAsync(
        string settingsGroupName,
        string key,
        CancellationToken cancellationToken = default);
}
