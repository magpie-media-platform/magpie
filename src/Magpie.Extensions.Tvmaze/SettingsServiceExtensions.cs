using System;
using System.Linq;
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
        var detail = await settingsService
            .GetExtensionGroupAsync(Constants.ExtensionId, ProviderSettings.GroupName, cancellationToken)
            .ConfigureAwait(false);
        var contact = detail?.Group.Settings
            .Where(setting => setting.Key.Equals(ProviderSettings.ContactKey, StringComparison.OrdinalIgnoreCase))
            .Select(setting => setting.Value)
            .LastOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Environment.GetEnvironmentVariable(EnvContactVariable);
        return new ProviderSettings { Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim() };
    }
}
