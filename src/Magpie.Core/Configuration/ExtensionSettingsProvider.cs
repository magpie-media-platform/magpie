using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Data;
using Magpie.Models.Result;

using static Magpie.Abstractions.Data.ISettingsRepository;

namespace Magpie.Core.Configuration;

/// <summary>
/// Provides access to extension-specific settings. Scoped to an extension.
/// </summary>
public sealed class ExtensionSettingsProvider(
    ISettingsRepository repository,
    Guid extensionId) : IExtensionSettingsProvider
{
    /// <inheritdoc />
    public Guid ExtensionId => extensionId;

    /// <inheritdoc />
    public Task<Result<string?>> GetSettingAsync(
        string settingsGroupName,
        string key,
        string? defaultValue,
        CancellationToken cancellationToken)
    {
        return repository
            .GetSettingAsync(extensionId, settingsGroupName, key, cancellationToken)
            .MapAsync(setting => setting?.Value ?? defaultValue);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyDictionary<string, string>>> GetSettingsAsync(
        string settingsGroupName,
        CancellationToken cancellationToken)
    {
        return repository
            .GetSettingsGroupAsync(extensionId, settingsGroupName, cancellationToken)
            .BindAsync(async group =>
            {
                if (group is null)
                {
                    return Result.Success<IReadOnlyDictionary<string, string>>(
                        ImmutableDictionary<string, string>.Empty);
                }
                return await repository
                    .GetAllSettingsAsync(cancellationToken)
                    .MapAsync(settings => settings
                        .Where(s => s.SettingsGroupId == group.Id)
                        .ToImmutableDictionary(s => s.Key, s => s.Value) as IReadOnlyDictionary<string, string>)
                    .ConfigureAwait(false);
            });
    }

    /// <inheritdoc />
    public Task<Result> SetSettingAsync(
        string settingsGroupName,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        return repository.UpsertSettingAsync(
            new SettingKey(extensionId, settingsGroupName, key),
            value,
            null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<bool>> DeleteSettingAsync(
        string settingsGroupName,
        string key,
        CancellationToken cancellationToken)
    {
        return repository.DeleteAsync(new SettingKey(extensionId, settingsGroupName, key), null, cancellationToken);
    }
}
