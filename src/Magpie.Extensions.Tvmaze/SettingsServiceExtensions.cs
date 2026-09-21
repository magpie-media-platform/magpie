using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Services;

namespace Magpie.Extensions.Tvmaze;

internal static class SettingsServiceExtensions
{
    private const string EnvContactVariable = "MAGPIE_TVMAZE_CONTACT";

    internal static async Task<ProviderSettings> ResolveTvmazeSettingsAsync(
        this ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var envContact = Environment.GetEnvironmentVariable(EnvContactVariable);
        if (!string.IsNullOrWhiteSpace(envContact))
        {
            resolved[ProviderSettings.ContactKey] = envContact!;
        }

        var detail = await settingsService
            .GetExtensionGroupAsync(Constants.ExtensionId, ProviderSettings.GroupName, cancellationToken)
            .ConfigureAwait(false);
        if (detail is not null)
        {
            foreach (var setting in detail.Group.Settings)
            {
                if (!string.IsNullOrWhiteSpace(setting.Value))
                {
                    resolved[setting.Key] = setting.Value;
                }
            }
        }

        return resolved.ToProviderSettings();
    }

    private static ProviderSettings ToProviderSettings(this IReadOnlyDictionary<string, string> settings)
    {
        settings.TryGetValue(ProviderSettings.ContactKey, out var contact);
        return new ProviderSettings { Contact = string.IsNullOrWhiteSpace(contact) ? null : contact!.Trim() };
    }
}
