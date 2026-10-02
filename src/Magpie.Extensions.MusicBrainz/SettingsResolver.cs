using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Services;

namespace Magpie.Extensions.MusicBrainz;

internal sealed class SettingsResolver(ISettingsService settingsService) : ISettingsResolver, IDisposable
{
    public const string EnvContactVariable = "MAGPIE_MUSICBRAINZ_CONTACT";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private Settings? _snapshot;

    public async Task<Settings> ResolveAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _snapshot ??= await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Settings> ReadAsync(CancellationToken cancellationToken)
    {
        var detail = await settingsService
            .GetExtensionGroupAsync(Constants.ExtensionId, Settings.GroupName, cancellationToken)
            .ConfigureAwait(false);
        var storedContact = detail?.Group.Settings
            .FirstOrDefault(setting =>
                string.Equals(setting.Key, Settings.ContactKey, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        var environmentContact = Environment.GetEnvironmentVariable(EnvContactVariable);
        var contact = string.IsNullOrWhiteSpace(environmentContact) ? storedContact : environmentContact;
        return new Settings { Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim() };
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
