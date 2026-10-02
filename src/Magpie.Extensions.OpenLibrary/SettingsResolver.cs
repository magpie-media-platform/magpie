using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Sdk.Services;

namespace Magpie.Extensions.OpenLibrary;

internal sealed class SettingsResolver(ISettingsService settingsService) : ISettingsResolver, IDisposable
{
    public const string EnvContactVariable = "MAGPIE_OPENLIBRARY_CONTACT";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private ProviderSettings? _snapshot;

    public async Task<RequestContext> GetRequestContextAsync(CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return RequestContext.ForContact(snapshot.Contact);
    }

    private async Task<ProviderSettings> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _snapshot ??= await ResolveSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProviderSettings> ResolveSnapshotAsync(CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var envContact = Environment.GetEnvironmentVariable(EnvContactVariable);
        if (!string.IsNullOrWhiteSpace(envContact))
        {
            resolved[ProviderSettings.ContactKey] = envContact;
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

        resolved.TryGetValue(ProviderSettings.ContactKey, out var contact);
        return new ProviderSettings
        {
            Contact = contact?.Trim()
        };
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
