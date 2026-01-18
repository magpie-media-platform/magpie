using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Data;
using Magpie.Models.Result;

using static Magpie.Abstractions.Data.ISettingsRepository;

namespace Magpie.Core.Configuration;

/// <summary>
/// Provides access to system-level settings groups.
/// </summary>
public sealed class SystemSettingsProvider(ISettingsRepository repository) : ISystemSettingsProvider
{
    /// <inheritdoc />
    public Task<Result<string?>> GetSettingAsync(
        string settingsGroupName,
        string key,
        string? defaultValue,
        CancellationToken cancellationToken)
    {
        return repository
            .GetSettingAsync(default, settingsGroupName, key, cancellationToken)
            .MapAsync(setting => setting?.Value ?? defaultValue);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyDictionary<string, string>>> GetSettingsAsync(
        string settingsGroupName,
        CancellationToken cancellationToken)
    {
        return repository
            .GetSettingsGroupAsync(Guid.Empty, settingsGroupName, cancellationToken)
            .BindAsync(async group =>
            {
                if (group == null)
                {
                    return Result.Success<IReadOnlyDictionary<string, string>>(
                        ImmutableDictionary<string, string>.Empty);
                }
                return await repository
                    .GetSettingsAsync(null, settingsGroupName, cancellationToken)
                    .MapAsync(settings => settings
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
            new SettingKey(default, settingsGroupName, key),
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
        return repository.DeleteAsync(new SettingKey(default, settingsGroupName, key), null, cancellationToken);
    }
}
