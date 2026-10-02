using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb;

internal sealed class SettingsResolver(
    ISettingsService settingsService,
    ILogger<SettingsResolver> logger) : ISettingsResolver, IDisposable
{
    public const string EnvApiKeyVariable = "MAGPIE_TMDB_API_KEY";

    public const string EnvLanguageVariable = "MAGPIE_TMDB_LANGUAGE";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private Settings? _snapshot;
    private bool _missingApiKeyWarned;

    private async Task<Settings> ResolveSnapshotAsync(CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var envKey = Environment.GetEnvironmentVariable(EnvApiKeyVariable);
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            resolved[Settings.ApiKeyKey] = envKey;
        }

        var envLanguage = Environment.GetEnvironmentVariable(EnvLanguageVariable);
        if (!string.IsNullOrWhiteSpace(envLanguage))
        {
            resolved[Settings.LanguageKey] = envLanguage;
        }

        var detail = await settingsService
            .GetExtensionGroupAsync(
                Constants.ExtensionId,
                Settings.GroupName,
                cancellationToken)
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

        return Settings.Parse(resolved);
    }

    public async Task<Settings> ReadSnapshotAsync(CancellationToken cancellationToken)
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

    public Task<RequestContext?> AcquireContextAsync(
        string? defaultLanguagePreference,
        CancellationToken cancellationToken)
        => AcquireContextAsync(null, defaultLanguagePreference, cancellationToken);

    public async Task<RequestContext?> AcquireContextAsync(
        IEnumerable<string>? ancestorLanguagePreferences,
        string? defaultLanguagePreference,
        CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.IsConfigured)
        {
            WarnMissingApiKey();
            return null;
        }

        return new RequestContext
        {
            ApiKey = snapshot.ApiKey,
            Language = ancestorLanguagePreferences?
                .FirstOrDefault(preference => !string.IsNullOrWhiteSpace(preference))?
                .Trim() ?? snapshot.ResolveLanguage(defaultLanguagePreference)
        };
    }

    public void Dispose() => _gate.Dispose();

    private void WarnMissingApiKey()
    {
        if (_missingApiKeyWarned)
        {
            return;
        }

        _missingApiKeyWarned = true;
        logger.LogWarning(
            "TMDb disabled: settings group {GroupName} has no {ApiKey} and environment variable {EnvVar} is not set",
            Settings.GroupName,
            Settings.ApiKeyKey,
            EnvApiKeyVariable);
    }
}
