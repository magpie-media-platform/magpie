using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;

namespace Magpie.Core.Configuration;

internal sealed class SettingsGroupWrapper(ISettingsRepository repository, SettingsGroup group) : ISettingsGroup
{
    public string Name => group.Name;

    public IDictionary<string, string> Settings { get; } = group.Settings.ToDictionary(s => s.Key, s => s.Value);

    public async Task<bool> SaveAsync(Guid? currentUserId, CancellationToken cancellationToken)
    {
        var remove = group.Settings.Select(s => s.Key).ToHashSet().Except(Settings.Keys);
        var update = new Dictionary<string, string>(Settings.Where(s => group.Settings.Any(gs =>
            gs.Key == s.Key &&
            gs.Value != s.Value)));
        var add = new Dictionary<string, string>(Settings.Where(s => group.Settings.All(gs =>
            gs.Key != s.Key)));

        return await repository.PatchAsync(
            new(group.ExtensionId, Name, new(add, remove, update)),
            currentUserId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ReloadAsync(CancellationToken cancellationToken)
    {
        group = await repository.GetByIdAsync(new(group.ExtensionId, group.Name), cancellationToken)
            .ConfigureAwait(false) ?? group;

        if (group.Settings is not { Count: > 0 })
        {
            if (Settings.Count is 0)
            {
                return false;
            }
            Settings.Clear();
            return true;
        }

        var changed = false;

        if (group.Settings.Count == Settings.Count)
        {
            foreach (var setting in group.Settings)
            {
                if (!Settings.TryGetValue(setting.Key, out var currentValue) || currentValue != setting.Value)
                {
                    changed = true;
                    break;
                }
            }
        }
        else
        {
            changed = true;
        }

        Settings.Clear();
        foreach (var storedSetting in group.Settings)
        {
            Settings[storedSetting.Key] = storedSetting.Value;
        }
        return changed;
    }
}
